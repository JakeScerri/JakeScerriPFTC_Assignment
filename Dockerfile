FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# Copy the key file - adjusted path to match your project structure
COPY pftc-jake_key.json /app/pftc-jake_key.json
ENV GOOGLE_APPLICATION_CREDENTIALS=/app/pftc-jake_key.json

# Set ASP.NET Core environment to Production
ENV ASPNETCORE_ENVIRONMENT=Production

# Explicitly set PORT environment variable
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:8080

# Add verbose diagnostics for startup
RUN echo "Container image built on $(date)"

ENTRYPOINT ["dotnet", "JakeScerriPFTC_Assignment.dll"]