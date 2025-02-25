# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копирование файла проекта и восстановление зависимостей
COPY ["AuthService/AuthService.csproj", "AuthService/"]
RUN dotnet restore "AuthService/AuthService.csproj"

# Копирование всего исходного кода
COPY . .
WORKDIR "/src/AuthService"

# Сборка и публикация приложения в папку /app/publish
RUN dotnet publish "AuthService.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuthService.dll"]
