# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Backend/AuthService/AuthService.csproj", "Backend/AuthService/"]
RUN dotnet restore "Backend/AuthService/AuthService.csproj"
COPY Backend/AuthService/. ./Backend/AuthService/
WORKDIR /src/Backend/AuthService
RUN dotnet publish -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "AuthService.dll"]
