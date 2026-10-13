-- Restricted login used for running practice SQL: data access only, no DDL.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'practice')
    CREATE LOGIN practice WITH PASSWORD = '$(PRACTICE_PASSWORD)', DEFAULT_DATABASE = AdventureWorksLT, CHECK_POLICY = OFF;
GO
USE AdventureWorksLT;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'practice')
    CREATE USER practice FOR LOGIN practice WITH DEFAULT_SCHEMA = SalesLT;
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::SalesLT TO practice;
GO
