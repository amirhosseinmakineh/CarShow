WITH Deduplicated AS
(
    SELECT
        Id,
        Metadata,
        ROW_NUMBER() OVER
        (
            PARTITION BY ChannelUsername, TelegramMessageId
            ORDER BY CollectedAt DESC, Id DESC
        ) AS RowNum
    FROM dbo.TelegramRawArchiveSnapshots
    WHERE ISJSON(Metadata) = 1
),
Archive AS
(
    SELECT
        A.Id,
        V.Model,
        V.Trim,
        V.Year,
        V.Price,
        V.IsSale,
        M.OriginalText
    FROM Deduplicated A
    CROSS APPLY OPENJSON(A.Metadata)
    WITH
    (
        Version INT,
        OriginalText NVARCHAR(MAX)
    ) M
    CROSS APPLY OPENJSON(A.Metadata, '$.Vehicles')
    WITH
    (
        Model NVARCHAR(200),
        Trim NVARCHAR(200),
        Year NVARCHAR(10),
        Price NVARCHAR(100),
        IsSale BIT
    ) V
    WHERE A.RowNum = 1
      AND M.Version = 2
),
UniqueAds AS
(
    SELECT DISTINCT
        Model,
        Trim,
        Year,
        Price,
        IsSale,
        OriginalText
    FROM Archive
),
Normalized AS
(
    SELECT
        TRIM(Model) AS Model,
        TRIM(Trim) AS Trim,
        TRANSLATE(
            TRIM(Year),
            N'۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩',
            N'01234567890123456789'
        ) AS RawYear,
        TRANSLATE(
            TRIM(Price),
            N'۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩',
            N'01234567890123456789'
        ) AS RawPrice
    FROM UniqueAds
    WHERE IsSale = 1
      AND ISNULL(TRIM(Model), N'') <> N''
      AND ISNULL(TRIM(Trim), N'') <> N''
),
PriceParts AS
(
    SELECT
        *,
        CASE
            WHEN RawPrice LIKE N'%میلیارد' THEN N'Billion'
            WHEN RawPrice LIKE N'%میلیون' THEN N'Million'
            ELSE N'Numeric'
        END AS PriceUnit,
        TRIM(
            REPLACE(REPLACE(RawPrice, N'میلیارد', N''), N'میلیون', N'')
        ) AS AmountText
    FROM Normalized
),
Converted AS
(
    SELECT
        Model,
        Trim,
        CASE
            WHEN TRY_CONVERT(INT, RawYear) BETWEEN 2000 AND 2099
                THEN TRY_CONVERT(INT, RawYear)
            WHEN TRY_CONVERT(INT, RawYear) BETWEEN 1300 AND 1499
                THEN TRY_CONVERT(INT, RawYear) + 621
            WHEN TRY_CONVERT(INT, RawYear) BETWEEN 400 AND 499
                THEN TRY_CONVERT(INT, RawYear) + 1621
            WHEN LEN(RawYear) = 2 AND RawYear LIKE '0[0-9]'
                THEN TRY_CONVERT(INT, RawYear) + 2021
            ELSE NULL
        END AS Year,
        CAST(
            CASE
                WHEN PriceUnit = N'Billion'
                    THEN TRY_CONVERT(
                        DECIMAL(19,9),
                        REPLACE(REPLACE(AmountText, '/', '.'), ',', '.')
                    )
                WHEN PriceUnit = N'Million'
                    THEN TRY_CONVERT(
                        DECIMAL(19,9),
                        REPLACE(REPLACE(AmountText, '/', '.'), ',', '.')
                    ) / 1000.0
                WHEN RawPrice COLLATE Latin1_General_100_BIN2
                    NOT LIKE '%[^0-9]%'
                    AND LEN(RawPrice) BETWEEN 4 AND 5
                    THEN TRY_CONVERT(DECIMAL(19,9), RawPrice) / 1000.0
                WHEN RawPrice COLLATE Latin1_General_100_BIN2
                    NOT LIKE '%[^0-9]%'
                    AND LEN(RawPrice) BETWEEN 6 AND 8
                    THEN TRY_CONVERT(DECIMAL(19,9), RawPrice) / 1000000.0
                ELSE NULL
            END
            AS DECIMAL(19,9)
        ) AS PriceBillion
    FROM PriceParts
),
Eligible AS
(
    SELECT *
    FROM Converted
    WHERE Year IS NOT NULL
      AND PriceBillion > 0
      AND PriceBillion < 1000
),
Ranked AS
(
    SELECT
        *,
        COUNT(*) OVER
        (
            PARTITION BY Model, Trim, Year
        ) AS TotalCount,
        ROW_NUMBER() OVER
        (
            PARTITION BY Model, Trim, Year
            ORDER BY PriceBillion
        ) AS PriceRank
    FROM Eligible
),
PriceStatistics AS
(
    SELECT
        Model,
        Trim,
        Year,
        MAX(TotalCount) AS TotalCount,
        MIN(PriceBillion) AS MinPrice,
        MAX(PriceBillion) AS MaxPrice,
        AVG(PriceBillion) AS AveragePrice,
        AVG(
            CASE
                WHEN PriceRank IN
                (
                    (TotalCount + 1) / 2,
                    (TotalCount + 2) / 2
                )
                THEN PriceBillion
            END
        ) AS MedianPrice
    FROM Ranked
    GROUP BY Model, Trim, Year
),
FinalResult AS
(
    SELECT
        *,
        'Approved' AS Status
    FROM PriceStatistics
)
SELECT
    Model,
    Trim,
    Year,
    Year - 621 AS PersianYear,
    TotalCount,
    TotalCount AS ValidCount,
    0 AS RemovedCount,
    CAST(MedianPrice AS DECIMAL(19,3)) AS MedianPriceBillion,
    CAST(MinPrice AS DECIMAL(19,3)) AS MinPriceBillion,
    CAST(MaxPrice AS DECIMAL(19,3)) AS MaxPriceBillion,
    CASE
        WHEN Status = 'Approved'
        THEN CAST(ROUND(AveragePrice, 3) AS DECIMAL(19,3))
    END AS ReferencePriceBillion,
    CASE
        WHEN Status = 'Approved'
        THEN CAST(ROUND(AveragePrice, 3) * 1000000000 AS BIGINT)
    END AS ReferencePriceToman,
    Status
FROM FinalResult
ORDER BY TotalCount DESC;
