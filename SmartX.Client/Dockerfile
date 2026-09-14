FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Client/SmartX.Client.csproj", "SmartX.Client/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Client/SmartX.Client.csproj"
COPY . .
WORKDIR /src/SmartX.Client
RUN dotnet publish -c Release -o /app/publish

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
