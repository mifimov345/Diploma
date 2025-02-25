# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["FileService/FileService.csproj", "FileService/"]
RUN dotnet restore "FileService/FileService.csproj"

COPY . .
WORKDIR "/src/FileService"
RUN dotnet publish "FileService.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FileService.dll"]
