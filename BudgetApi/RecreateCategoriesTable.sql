-- Drop and recreate the Categories table with Value and Description columns
USE RenovationBudgetDB;
GO

IF OBJECT_ID('dbo.Categories', 'U') IS NOT NULL
    DROP TABLE dbo.Categories;
GO

CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Value NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255) NOT NULL
);
GO

INSERT INTO Categories (Value, Description) VALUES
('Securing and Demolition', 'Demolition'),
('Kitchen', 'Kitchen Renovation'),
('Bathroom', 'Bathroom Renovation'),
('Flooring', 'Flooring Work'),
('Painting', 'Painting Work'),
('Electrical', 'Electrical Work'),
('Plumbing', 'Plumbing Work'),
('HVAC', 'Heating, Ventilation, and Air Conditioning'),
('Windows/Doors', 'Windows and Doors'),
('Roofing', 'Roofing Work'),
('Landscaping', 'Landscaping'),
('Other', 'Other Expenses');
GO
