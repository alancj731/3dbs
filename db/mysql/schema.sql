-- AdventureWorksLT (SalesLT) for MySQL 8.4.
-- MySQL has no schemas inside a database, so the database itself is named SalesLT;
-- that way `SalesLT.Customer` means the same thing as on SQL Server.
-- Data is copied from SQL Server by the API's Seeder.

DROP DATABASE IF EXISTS SalesLT;
CREATE DATABASE SalesLT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE SalesLT;

CREATE TABLE ProductCategory (
  ProductCategoryID       INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  ParentProductCategoryID INT NULL,
  Name                    VARCHAR(50) NOT NULL,
  rowguid                 CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate            DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_ProductCategory_Name (Name),
  UNIQUE KEY AK_ProductCategory_rowguid (rowguid),
  CONSTRAINT FK_ProductCategory_Parent FOREIGN KEY (ParentProductCategoryID) REFERENCES ProductCategory (ProductCategoryID)
);

CREATE TABLE ProductModel (
  ProductModelID INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  Name           VARCHAR(50) NOT NULL,
  rowguid        CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_ProductModel_Name (Name),
  UNIQUE KEY AK_ProductModel_rowguid (rowguid)
);

CREATE TABLE ProductDescription (
  ProductDescriptionID INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  Description          VARCHAR(400) NOT NULL,
  rowguid              CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_ProductDescription_rowguid (rowguid)
);

CREATE TABLE ProductModelProductDescription (
  ProductModelID       INT NOT NULL,
  ProductDescriptionID INT NOT NULL,
  Culture              CHAR(6) NOT NULL,
  rowguid              CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (ProductModelID, ProductDescriptionID, Culture),
  UNIQUE KEY AK_ProductModelProductDescription_rowguid (rowguid),
  CONSTRAINT FK_PMPD_ProductModel FOREIGN KEY (ProductModelID) REFERENCES ProductModel (ProductModelID),
  CONSTRAINT FK_PMPD_ProductDescription FOREIGN KEY (ProductDescriptionID) REFERENCES ProductDescription (ProductDescriptionID)
);

CREATE TABLE Product (
  ProductID              INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  Name                   VARCHAR(50) NOT NULL,
  ProductNumber          VARCHAR(25) NOT NULL,
  Color                  VARCHAR(15) NULL,
  StandardCost           DECIMAL(19,4) NOT NULL,
  ListPrice              DECIMAL(19,4) NOT NULL,
  Size                   VARCHAR(5) NULL,
  Weight                 DECIMAL(8,2) NULL,
  ProductCategoryID      INT NULL,
  ProductModelID         INT NULL,
  SellStartDate          DATETIME(3) NOT NULL,
  SellEndDate            DATETIME(3) NULL,
  DiscontinuedDate       DATETIME(3) NULL,
  ThumbnailPhotoFileName VARCHAR(50) NULL,
  rowguid                CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate           DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_Product_Name (Name),
  UNIQUE KEY AK_Product_ProductNumber (ProductNumber),
  UNIQUE KEY AK_Product_rowguid (rowguid),
  CONSTRAINT FK_Product_ProductCategory FOREIGN KEY (ProductCategoryID) REFERENCES ProductCategory (ProductCategoryID),
  CONSTRAINT FK_Product_ProductModel FOREIGN KEY (ProductModelID) REFERENCES ProductModel (ProductModelID),
  CONSTRAINT CK_Product_StandardCost CHECK (StandardCost >= 0.00),
  CONSTRAINT CK_Product_ListPrice CHECK (ListPrice >= 0.00),
  CONSTRAINT CK_Product_Weight CHECK (Weight > 0.00),
  CONSTRAINT CK_Product_SellEndDate CHECK (SellEndDate >= SellStartDate OR SellEndDate IS NULL)
);

CREATE TABLE Customer (
  CustomerID   INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  NameStyle    BOOLEAN NOT NULL DEFAULT 0,
  Title        VARCHAR(8) NULL,
  FirstName    VARCHAR(50) NOT NULL,
  MiddleName   VARCHAR(50) NULL,
  LastName     VARCHAR(50) NOT NULL,
  Suffix       VARCHAR(10) NULL,
  CompanyName  VARCHAR(128) NULL,
  SalesPerson  VARCHAR(256) NULL,
  EmailAddress VARCHAR(50) NULL,
  Phone        VARCHAR(25) NULL,
  PasswordHash VARCHAR(128) NOT NULL,
  PasswordSalt VARCHAR(10) NOT NULL,
  rowguid      CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_Customer_rowguid (rowguid),
  KEY IX_Customer_EmailAddress (EmailAddress)
);

CREATE TABLE Address (
  AddressID     INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  AddressLine1  VARCHAR(60) NOT NULL,
  AddressLine2  VARCHAR(60) NULL,
  City          VARCHAR(30) NOT NULL,
  StateProvince VARCHAR(50) NOT NULL,
  CountryRegion VARCHAR(50) NOT NULL,
  PostalCode    VARCHAR(15) NOT NULL,
  rowguid       CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_Address_rowguid (rowguid)
);

CREATE TABLE CustomerAddress (
  CustomerID   INT NOT NULL,
  AddressID    INT NOT NULL,
  AddressType  VARCHAR(50) NOT NULL,
  rowguid      CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (CustomerID, AddressID),
  UNIQUE KEY AK_CustomerAddress_rowguid (rowguid),
  CONSTRAINT FK_CustomerAddress_Customer FOREIGN KEY (CustomerID) REFERENCES Customer (CustomerID),
  CONSTRAINT FK_CustomerAddress_Address FOREIGN KEY (AddressID) REFERENCES Address (AddressID)
);

CREATE TABLE SalesOrderHeader (
  SalesOrderID           INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  RevisionNumber         TINYINT UNSIGNED NOT NULL DEFAULT 0,
  OrderDate              DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DueDate                DATETIME(3) NOT NULL,
  ShipDate               DATETIME(3) NULL,
  Status                 TINYINT UNSIGNED NOT NULL DEFAULT 1,
  OnlineOrderFlag        BOOLEAN NOT NULL DEFAULT 1,
  -- Computed on SQL Server; MySQL can't compute from an AUTO_INCREMENT column, so it is stored.
  SalesOrderNumber       VARCHAR(25) NULL,
  PurchaseOrderNumber    VARCHAR(25) NULL,
  AccountNumber          VARCHAR(15) NULL,
  CustomerID             INT NOT NULL,
  ShipToAddressID        INT NULL,
  BillToAddressID        INT NULL,
  ShipMethod             VARCHAR(50) NOT NULL,
  CreditCardApprovalCode VARCHAR(15) NULL,
  SubTotal               DECIMAL(19,4) NOT NULL DEFAULT 0.00,
  TaxAmt                 DECIMAL(19,4) NOT NULL DEFAULT 0.00,
  Freight                DECIMAL(19,4) NOT NULL DEFAULT 0.00,
  TotalDue               DECIMAL(19,4) AS (IFNULL(SubTotal + TaxAmt + Freight, 0)) STORED,
  Comment                TEXT NULL,
  rowguid                CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate           DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UNIQUE KEY AK_SalesOrderHeader_rowguid (rowguid),
  UNIQUE KEY AK_SalesOrderHeader_SalesOrderNumber (SalesOrderNumber),
  CONSTRAINT FK_SalesOrderHeader_Customer FOREIGN KEY (CustomerID) REFERENCES Customer (CustomerID),
  CONSTRAINT FK_SalesOrderHeader_ShipTo FOREIGN KEY (ShipToAddressID) REFERENCES Address (AddressID),
  CONSTRAINT FK_SalesOrderHeader_BillTo FOREIGN KEY (BillToAddressID) REFERENCES Address (AddressID),
  CONSTRAINT CK_SalesOrderHeader_Status CHECK (Status BETWEEN 0 AND 8),
  CONSTRAINT CK_SalesOrderHeader_DueDate CHECK (DueDate >= OrderDate),
  CONSTRAINT CK_SalesOrderHeader_ShipDate CHECK (ShipDate >= OrderDate OR ShipDate IS NULL),
  CONSTRAINT CK_SalesOrderHeader_SubTotal CHECK (SubTotal >= 0.00),
  CONSTRAINT CK_SalesOrderHeader_TaxAmt CHECK (TaxAmt >= 0.00),
  CONSTRAINT CK_SalesOrderHeader_Freight CHECK (Freight >= 0.00)
);

CREATE TABLE SalesOrderDetail (
  SalesOrderID       INT NOT NULL,
  SalesOrderDetailID INT NOT NULL AUTO_INCREMENT,
  OrderQty           SMALLINT NOT NULL,
  ProductID          INT NOT NULL,
  UnitPrice          DECIMAL(19,4) NOT NULL,
  UnitPriceDiscount  DECIMAL(19,4) NOT NULL DEFAULT 0.0,
  LineTotal          DECIMAL(38,6) AS (IFNULL(UnitPrice * (1.0 - UnitPriceDiscount) * OrderQty, 0.0)) STORED,
  rowguid            CHAR(36) NOT NULL DEFAULT (UUID()),
  ModifiedDate       DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (SalesOrderDetailID),
  UNIQUE KEY PK_SalesOrderDetail (SalesOrderID, SalesOrderDetailID),
  UNIQUE KEY AK_SalesOrderDetail_rowguid (rowguid),
  CONSTRAINT FK_SalesOrderDetail_Header FOREIGN KEY (SalesOrderID) REFERENCES SalesOrderHeader (SalesOrderID) ON DELETE CASCADE,
  CONSTRAINT FK_SalesOrderDetail_Product FOREIGN KEY (ProductID) REFERENCES Product (ProductID),
  CONSTRAINT CK_SalesOrderDetail_OrderQty CHECK (OrderQty > 0),
  CONSTRAINT CK_SalesOrderDetail_UnitPrice CHECK (UnitPrice >= 0.00),
  CONSTRAINT CK_SalesOrderDetail_UnitPriceDiscount CHECK (UnitPriceDiscount >= 0.00)
);

-- Restricted login used for running practice SQL: data access only, no DDL.
CREATE USER IF NOT EXISTS 'practice'@'%' IDENTIFIED BY '__PRACTICE_PASSWORD__';
GRANT SELECT, INSERT, UPDATE, DELETE ON SalesLT.* TO 'practice'@'%';
