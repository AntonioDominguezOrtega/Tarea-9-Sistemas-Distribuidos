# Etapa 1: Compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Etapa 2: Ejecución (Imagen base de Azure Functions para Linux)
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0

# Variables de entorno necesarias para Azure Functions
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

# 1. Copiamos el código compilado (los DLLs y configuraciones)
COPY --from=build /app/publish /home/site/wwwroot

# 2. CORRECCIÓN: Copiamos explícitamente la carpeta wwwroot (HTML, JS, Imágenes)
# Destino: /home/site/wwwroot/wwwroot
COPY wwwroot /home/site/wwwroot/wwwroot