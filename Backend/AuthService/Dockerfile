# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Копируем csproj и восстанавливаем зависимости
COPY ./Backend/AuthService/AuthService/AuthService.csproj ./AuthService/
RUN ls -l /src/AuthService && dotnet restore "/src/AuthService/AuthService.csproj"

# Копируем все файлы и публикуем проект
COPY . .
WORKDIR /src/AuthService
RUN ls -l /src/AuthService && dotnet publish "/src/AuthService/AuthService.csproj" -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Копируем результаты сборки из этапа сборки
COPY --from=build /app/publish .

# Запускаем приложение
ENTRYPOINT ["dotnet", "AuthService.dll"]
