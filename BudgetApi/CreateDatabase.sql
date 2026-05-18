-- Run this script in SQL Server Management Studio or Azure Data Studio
-- This will create the database and the table for your renovation budget tracker

CREATE DATABASE RenovationBudgetDB;
GO

USE RenovationBudgetDB;
GO

CREATE TABLE BudgetItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Category NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255) NOT NULL,
    Budget DECIMAL(18,2) NOT NULL,
    Actual DECIMAL(18,2) NOT NULL,
    Date DATE NULL,
    Vendor NVARCHAR(100) NULL,
    Status NVARCHAR(50) NOT NULL
);
GO
