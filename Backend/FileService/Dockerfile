# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Копируем csproj и восстанавливаем зависимости
COPY Backend/FileService/FileService.csproj ./FileService/
RUN ls -l /src/FileService && dotnet restore "/src/FileService/FileService.csproj"

# Копируем все файлы и публикуем проект
COPY . .
WORKDIR /src/FileService
RUN ls -l /src/FileService && dotnet publish "/src/FileService/FileService.csproj" -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Копируем результаты сборки из этапа сборки
COPY --from=build /app/publish .

# Запускаем приложение
ENTRYPOINT ["dotnet", "FileService.dll"]
