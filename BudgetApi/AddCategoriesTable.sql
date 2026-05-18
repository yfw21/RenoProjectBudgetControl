-- Add this to your SQL Server to create the Categories table
USE RenovationBudgetDB;
GO

CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL
);
GO
