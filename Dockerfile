FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

COPY pftc-2025-leon-c6d5aa81fcc1.json /app/pftc-2025-leon-c6d5aa81fcc1.json
ENV GOOGLE_APPLICATION_CREDENTIALS=/app/pftc-2025-leon-c6d5aa81fcc1.json

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080/

ENTRYPOINT ["dotnet", "TicketingSystem.dll"]