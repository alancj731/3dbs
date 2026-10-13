# Build context is the repository root (it needs db/ for the MySQL/Postgres schema files):
#   docker build -f api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api/Api.csproj api/
RUN dotnet restore api/Api.csproj
COPY api/ api/
RUN dotnet publish api/Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY db/mysql/schema.sql db/mysql/schema.sql
COPY db/postgres/schema.sql db/postgres/schema.sql
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    SchemaDir=/app/db
EXPOSE 8080
# The aspnet image ships a non-root "app" user.
USER app
ENTRYPOINT ["dotnet", "Api.dll"]
