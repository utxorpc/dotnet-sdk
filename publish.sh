#!/bin/bash
# Clean and build the project
source .env
rm -rf ./src/Utxorpc.Sdk/bin/
rm -rf ./src/Utxorpc.Sdk/obj

dotnet clean
dotnet build --configuration Release

# Pack your project
dotnet pack --configuration Release

PACKAGE_FILE=$(find ./src/Utxorpc.Sdk/bin/Release -name "*.nupkg" | head -n 1)

if [ -z "$PACKAGE_FILE" ]; then
    echo "No .nupkg file found."
    exit 1
fi

echo "Found package file: $PACKAGE_FILE"
dotnet nuget push "$PACKAGE_FILE" --api-key ${NUGET_API_KEY} --source https://api.nuget.org/v3/index.json