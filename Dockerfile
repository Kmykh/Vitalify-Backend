# syntax=docker/dockerfile:1

# ---- Compilación ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Primero solo los archivos de proyecto, para cachear el restore.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/Vitalify.Domain/Vitalify.Domain.csproj src/Vitalify.Domain/
COPY src/Vitalify.Application/Vitalify.Application.csproj src/Vitalify.Application/
COPY src/Vitalify.Infrastructure/Vitalify.Infrastructure.csproj src/Vitalify.Infrastructure/
COPY src/Vitalify.Api/Vitalify.Api.csproj src/Vitalify.Api/
RUN dotnet restore src/Vitalify.Api/Vitalify.Api.csproj

COPY src/ src/
RUN dotnet publish src/Vitalify.Api/Vitalify.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Ejecución ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
# Usuario sin privilegios que trae la imagen oficial (app, UID 1654).
USER $APP_UID
ENTRYPOINT ["dotnet", "Vitalify.Api.dll"]
