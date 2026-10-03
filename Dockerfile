# ====================================================================
# Dockerfile para Render - Sistema de Gestión de Matrículas Académicas
# .NET 10 | Universidad del Sinú
# ====================================================================

# 1. Imagen base del SDK para compilar y generar la publicación
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar archivo .csproj y restaurar dependencias
COPY *.csproj ./
RUN dotnet restore

# Copiar el código fuente completo y compilar en modo Release
COPY . ./
RUN dotnet publish -c Release -o /app/out

# 2. Imagen base ligera de ejecución (runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copiar los artefactos compilados desde la etapa de build
COPY --from=build /app/out .

# Configurar puerto para Render (escucha en 8080 y en la variable PORT de Render)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Iniciar la aplicación
ENTRYPOINT ["dotnet", "MatriculasApp.dll"]
