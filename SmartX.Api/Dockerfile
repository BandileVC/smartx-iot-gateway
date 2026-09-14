FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Api/SmartX.Api.csproj", "SmartX.Api/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Api/SmartX.Api.csproj"
COPY . .
WORKDIR /src/SmartX.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5050
ENV ASPNETCORE_URLS=http://+:5050
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
