# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SearchService/SearchService.csproj", "SearchService/"]
RUN dotnet restore "SearchService/SearchService.csproj"

COPY . .
WORKDIR "/src/SearchService"
RUN dotnet publish "SearchService.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SearchService.dll"]
