dotnet publish ./ClemToken.csproj --configuration Release --runtime win-x64 -o ./bin/ /p:PublishSingleFile=true /p:SelfContained=true /p:OutputType=Exe
move .\bin\ClemToken.exe .\bin\ClemToken-windows.exe
dotnet publish ./ClemToken.csproj --configuration Release --runtime win-x64 -o ./bin/ /p:OutputType=Exe /p:Clean=false
move .\bin\ClemToken.exe .\bin\ClemToken-windows-light.exe
dotnet publish ./ClemToken.csproj --configuration Release --runtime win-x64 -o ./bin/ /p:OutputType=Library /p:Clean=false
move .\bin\ClemToken.dll .\bin\ClemToken-library-windows.dll
dotnet publish ./ClemToken.csproj --configuration Release --runtime linux-x64 -o ./bin/ /p:PublishSingleFile=true /p:SelfContained=true /p:OutputType=Exe /p:Clean=false
move .\bin\ClemToken .\bin\ClemToken-linux
dotnet publish ./ClemToken.csproj --configuration Release --runtime linux-x64 -o ./bin/ /p:OutputType=Exe /p:Clean=false
move .\bin\ClemToken .\bin\ClemToken-linux-light
dotnet publish ./ClemToken.csproj --configuration Release --runtime linux-x64 -o ./bin/ /p:OutputType=Library /p:Clean=false
move .\bin\ClemToken.dll .\bin\ClemToken-library-linux.dll