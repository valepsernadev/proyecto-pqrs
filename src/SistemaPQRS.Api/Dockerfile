# ============================================================
# Etapa 1: build — SDK completo, restaura y publica en Release
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar primero con solo el .csproj: si los paquetes no
# cambian, Docker reutiliza esta capa cacheada y no re-restaura
COPY SistemaPQRS.Api.csproj .
RUN dotnet restore SistemaPQRS.Api.csproj

# Copiar el código restante y publicar
COPY . .
RUN dotnet publish SistemaPQRS.Api.csproj -c Release -o /app/publish --no-restore

# ============================================================
# Etapa 2: runtime — solo el runtime ASP.NET, mucho mas chico
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SistemaPQRS.Api.dll"]
