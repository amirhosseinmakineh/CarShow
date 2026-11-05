SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
-- Owner-approved source registry, 2026-11-04. Idempotent; no deletes.
DECLARE @Approved TABLE (Identifier nvarchar(256) NOT NULL PRIMARY KEY);
INSERT INTO @Approved (Identifier) VALUES
(N'autopalermo'),
(N'davoodkelasii'),
(N'tajcarsefr'),
(N'autozamanzade'),
(N'miladforoud'),
(N'autoland41'),
(N'haminkhodroo'),
(N'adib_khodro_qom'),
(N'gallery20moradi'),
(N'car_sales_gandomi'),
(N'autosolhi'),
(N'behzadsaberi_oxinkhodro'),
(N'auto_ap'),
(N'avatttgalry'),
(N'tehrankhodro97'),
(N'mohamadrezamansoor'),
(N'payamkhodrooo'),
(N'auto_teymouri'),
(N'shahabbesharaty'),
(N'autoesmieh'),
(N'omidabbasicarsale'),
(N'otoeiliya'),
(N'autogalerymhmood'),
(N'kharid0forosh0mashin'),
(N'eshraghicar'),
(N'irankodroohadiali'),
(N'namayeshgahdarantehran21'),
(N'hashemi_car2'),
(N'bazarkhodroh'),
(N'modern_khodrooooo'),
(N'thehhrankhodrow'),
(N'autoplack'),
(N'automarkethamidnasiri'),
(N'goroohmashin'),
(N'khodro_mashin_tehran_divar_bama'),
(N'otosefr_farzadakbari'),
(N'rafieicar'),
(N'hhirsa'),
(N'wolfkhodro'),
(N'sefidgarali'),
(N'aradkhodro96'),
(N'payamrahnama'),
(N'uotobehroz'),
(N'autoluxy'),
(N'otoadelkhani'),
(N'autogorgangolbini'),
(N'ehsaaant'),
(N'autoramin9787'),
(N'automeysamsaghi'),
(N'auto_dayy'),
(N'shakiba_iran_16'),
(N'hoseinbabaieqom'),
(N'auto_royal_meshkin'),
(N'auto_najafiqom'),
(N'mehrdadloghmanii'),
(N'alavicarexhibition'),
(N'mamatir_khodro_hosseini'),
(N'grohcobrotehran'),
(N'khazarrkhodro'),
(N'auto_1abdolmaleki1'),
(N'autopasdaran'),
(N'hakimghailarrr'),
(N'auto_yektaa'),
(N'jamshidsamadiiii'),
(N'auto_jahan'),
(N'ilam98khodro'),
(N'autosardarr'),
(N'khodro_nasri'),
(N'+kF0Dpp9cMl45MjNk'),
(N'autovalayati'),
(N'outorashidiyan'),
(N'sahracar'),
(N'arasmehri'),
(N'parsiankhodroqolizade'),
(N'kiu2016'),
(N'arsinkhodro'),
(N'bakshabadi'),
(N'automojtaba66'),
(N'autoeightasgarinia'),
(N'keyvancar'),
(N'pejman2616'),
(N'auto_tabrizi'),
(N'hesamfatemikaraj'),
(N'autofezolahi'),
(N'irankhodromonirikashan');
-- Reuse historical source IDs when URL scheme, case or trailing slash differs.
-- Duplicate historical rows remain in place and are paused; their messages are not moved or deleted.
DECLARE @Candidates TABLE (SourceId bigint NOT NULL, Identifier nvarchar(256) NOT NULL);
INSERT INTO @Candidates (SourceId, Identifier)
SELECT c.Id, CASE
 WHEN LEFT(LTRIM(RTRIM(v.Value)),1) = '+' THEN LTRIM(RTRIM(v.Value))
 WHEN CHARINDEX('/+', LTRIM(RTRIM(v.Value))) > 0 THEN SUBSTRING(LTRIM(RTRIM(v.Value)), CHARINDEX('/+', LTRIM(RTRIM(v.Value))) + 1, 256)
 ELSE REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(v.Value))), N'https://www.t.me/', N''), N'http://www.t.me/', N''), N'https://t.me/', N''), N'http://t.me/', N''), N'https://telegram.me/', N''), N'http://telegram.me/', N'') END
FROM TelegramChannels c CROSS APPLY (VALUES(c.NormalizedIdentifier),(c.TelegramIdentifier),(c.Username),(c.PublicUrl)) v(Value)
WHERE v.Value IS NOT NULL AND LEN(LTRIM(RTRIM(v.Value))) > 0 AND LEN(v.Value) <= 256;
WHILE EXISTS (SELECT 1 FROM @Candidates WHERE RIGHT(Identifier,1) = '/')
 UPDATE @Candidates SET Identifier = LEFT(Identifier,LEN(Identifier)-1) WHERE RIGHT(Identifier,1) = '/';
UPDATE @Candidates SET Identifier = SUBSTRING(Identifier,2,256) WHERE LEFT(Identifier,1) = '@';
UPDATE @Candidates SET Identifier = SUBSTRING(Identifier,3,256) WHERE LEFT(Identifier,2) = 's/';
DECLARE @Matches TABLE (SourceId bigint NOT NULL, Identifier nvarchar(256) NOT NULL);
INSERT INTO @Matches SELECT DISTINCT c.SourceId, a.Identifier FROM @Candidates c JOIN @Approved a
 ON c.Identifier COLLATE Latin1_General_100_BIN2 = a.Identifier COLLATE Latin1_General_100_BIN2;
DECLARE @ConflictingSource bigint = (SELECT TOP(1) SourceId FROM @Matches GROUP BY SourceId HAVING COUNT(*) > 1 ORDER BY SourceId);
IF @ConflictingSource IS NOT NULL
BEGIN
 DECLARE @SourceConflictMessage nvarchar(2048) = N'Conflicting source metadata. Review source ID ' + CONVERT(nvarchar(32), @ConflictingSource) + N' before retrying this migration.';
 THROW 51001, @SourceConflictMessage, 1;
END;
DECLARE @PrimarySources TABLE (Identifier nvarchar(256) NOT NULL PRIMARY KEY, SourceId bigint NOT NULL);
INSERT INTO @PrimarySources
SELECT m.Identifier, COALESCE(MIN(CASE WHEN c.NormalizedIdentifier COLLATE Latin1_General_100_BIN2 = m.Identifier COLLATE Latin1_General_100_BIN2 THEN c.Id END), MIN(c.Id))
FROM @Matches m JOIN TelegramChannels c ON c.Id = m.SourceId GROUP BY m.Identifier;
UPDATE c SET Enabled = 0, Status = N'Paused', LastError = N'DuplicateSourceAlias: use canonical source ' + CONVERT(nvarchar(32),p.SourceId), UpdatedAt = SYSUTCDATETIME()
FROM TelegramChannels c JOIN @Matches m ON m.SourceId = c.Id JOIN @PrimarySources p ON p.Identifier = m.Identifier WHERE c.Id <> p.SourceId;
UPDATE c SET NormalizedIdentifier = p.Identifier, TelegramIdentifier = p.Identifier,
 Username = CASE WHEN LEFT(p.Identifier,1) = '+' THEN N'' ELSE p.Identifier END, UpdatedAt = SYSUTCDATETIME()
FROM TelegramChannels c JOIN @PrimarySources p ON p.SourceId = c.Id;

UPDATE c SET Enabled = 0, Status = N'Paused', UpdatedAt = SYSUTCDATETIME()
FROM TelegramChannels c WHERE NOT EXISTS (SELECT 1 FROM @Approved a WHERE a.Identifier = c.NormalizedIdentifier);
INSERT INTO TelegramChannels
(IsDelete, TelegramIdentifier, NormalizedIdentifier, Username, PublicUrl, Title, Status, Enabled, CollectorType,
 MessagesCollected, ListingsExtracted, RetryCount, CreatedAt, UpdatedAt, LastError)
SELECT 0, a.Identifier, a.Identifier, CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'' ELSE a.Identifier END,
 N'https://t.me/' + a.Identifier, N'', CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'AccessRequired' ELSE N'Pending' END,
 1, N'telegram', 0, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(),
 CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'Private invite: public preview access is unavailable.' ELSE NULL END
FROM @Approved a WHERE NOT EXISTS (SELECT 1 FROM TelegramChannels c WHERE c.NormalizedIdentifier = a.Identifier);
-- Existing approved sources retain their manager-selected enabled/deleted state.
-- A deleted approved source remains historical until an admin explicitly reimports it.
-- ZeroVehicleChannels remains a checkpoint table only; it is not a source authority.
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
