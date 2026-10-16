# ============================================================
# Stage 1: Build & publish aplikasi
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj dulu saja supaya layer "dotnet restore" bisa di-cache
# selama daftar package (Dapper, ClosedXML, dll) belum berubah.
COPY FitToWorkApp.csproj .
RUN dotnet restore "FitToWorkApp.csproj"

# Baru copy semua source code, lalu publish.
COPY . .
RUN dotnet publish "FitToWorkApp.csproj" -c Release -o /app/publish --no-restore

# ============================================================
# Stage 2: Runtime image (lebih kecil, tanpa SDK)
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl dipakai oleh HEALTHCHECK di docker-compose.production.yml
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FitToWorkApp.dll"]
