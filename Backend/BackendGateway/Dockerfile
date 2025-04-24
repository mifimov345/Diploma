 # Build stage
 FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
 WORKDIR /src

# Копируем .csproj файл шлюза
 COPY ["BackendGateway/BackendGateway.csproj", "BackendGateway/"]

# Копируем ТОЛЬКО файлы конфигурации шлюза ЯВНО
# Убедись, что эти файлы существуют в BackendGateway/ относительно контекста сборки (./Backend)
COPY ["BackendGateway/appsettings.json", "BackendGateway/"]
COPY ["BackendGateway/appsettings.Development.json", "BackendGateway/"]

# Восстанавливаем зависимости ПОСЛЕ копирования конфигурации (на всякий случай)
 RUN dotnet restore "BackendGateway/BackendGateway.csproj"

#COPY . . # Убираем копирование ВСЕГО, чтобы избежать случайных перезаписей
# Копируем все остальные исходные файлы шлюза
COPY ["BackendGateway/", "BackendGateway/"]

# Копируем все исходные файлы ДРУГИХ проектов, если шлюз от них зависит
# Например, если есть общие модели:
# COPY ["SharedModels/", "SharedModels/"]
# COPY ["SharedModels/SharedModels.csproj", "SharedModels/"]
# ... и так далее для всех зависимостей ...
# Если зависимостей нет, эти строки не нужны.

# Устанавливаем рабочую директорию для publish
 WORKDIR "/src/BackendGateway"

# Публикуем проект
 RUN dotnet publish "BackendGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

 # Runtime stage
 FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
 WORKDIR /app
 EXPOSE 80
 EXPOSE 443
# Копируем ТОЛЬКО результат публикации
 COPY --from=build /app/publish .
# Теперь файлы appsettings.*.json должны быть включены в /app/publish dotnet'ом,
# так как они были скопированы на предыдущей стадии и свойство CopyToPublishDirectory по умолчанию PreserveNewest

 ENTRYPOINT ["dotnet", "BackendGateway.dll"]