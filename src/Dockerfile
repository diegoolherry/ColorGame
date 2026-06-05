# Etapa de construcción (Build)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copiar el archivo de proyecto y restaurar dependencias
COPY ["ColorGame.csproj", "./"]
RUN dotnet restore "./ColorGame.csproj"

# Copiar el resto del código y compilar
COPY . .
RUN dotnet publish "ColorGame.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa final (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Configuración para Render: Escuchar en el puerto 10000 por defecto
# Render inyecta la variable de entorno PORT, pero por defecto usa la 10000
ENV ASPNETCORE_URLS=http://+:10000

# Exponer el puerto
EXPOSE 10000

ENTRYPOINT ["dotnet", "ColorGame.dll"]
