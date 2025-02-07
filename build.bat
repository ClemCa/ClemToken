dotnet publish --configuration Release --runtime win-x64 -o ./bin/ /p:PublishSingleFile=true /p:SelfContained=true /p:AssemblyName=ClemToken-windows
dotnet publish --configuration Release --runtime linux-x64 -o ./bin/ /p:PublishSingleFile=true /p:SelfContained=true /p:AssemblyName=ClemToken-linux
dotnet publish --configuration Release --runtime win-x64 -o ./bin/ /p:AssemblyName=ClemToken-library-windows /p:OutputType=Library
dotnet publish --configuration Release --runtime linux-x64 -o ./bin/ /p:AssemblyName=ClemToken-library-linux /p:OutputType=Library