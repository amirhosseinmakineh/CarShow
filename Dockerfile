FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish CarShow/CarShow.csproj -c Release -o /app/publish --no-self-contained
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production PORT=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "CarShow.dll"]
