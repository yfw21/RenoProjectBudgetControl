CREATE TABLE CategoryActivities (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CategoryValue NVARCHAR(100) NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    WorkHours INT NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL
);
GO
