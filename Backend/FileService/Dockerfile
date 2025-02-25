# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Backend/FileService/FileService.csproj", "Backend/FileService/"]
RUN dotnet restore "Backend/FileService/FileService.csproj"
COPY Backend/FileService/. ./Backend/FileService/
WORKDIR /src/Backend/FileService
RUN dotnet publish -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "FileService.dll"]
