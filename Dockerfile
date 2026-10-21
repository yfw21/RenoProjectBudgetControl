# One image: the ASP.NET Core API serves the built React SPA from wwwroot.

# ---- 1. Build the React frontend ---------------------------------------------
FROM node:20-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# vite.config.ts writes the bundle to ../BudgetApi/wwwroot
RUN npm run build

# ---- 2. Publish the .NET API -------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src/BudgetApi
COPY BudgetApi/BudgetApi.csproj ./
RUN dotnet restore
COPY BudgetApi/ ./
COPY --from=web /src/BudgetApi/wwwroot ./wwwroot
RUN dotnet publish -c Release -o /app --no-restore

# ---- 3. Runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# Tesseract powers receipt OCR (Services/Receipts/TesseractOcr.cs shells out to it).
RUN apt-get update \
 && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /app ./
# Built-in non-root user from the aspnet image; SQLite/uploads (local mode only) go under /data.
RUN mkdir -p /data/uploads && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__DefaultConnection="Data Source=/data/reno.db" \
    Files__Root=/data/uploads
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s CMD curl -fs http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "BudgetApi.dll"]
