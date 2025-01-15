@echo off
echo Which test would you like to run?
echo (G)enerator
echo (P)arser
set /p choice=Enter your choice:

if /i "%choice%"=="G" (
    echo Running Generator test...
    dotnet run -test test_generator.txt
) else if /i "%choice%"=="g" (
    echo Running Generator test...
    dotnet run -test test_generator.txt
) else if /i "%choice%"=="P" (
    echo Running Parser test...
    dotnet run -testP test_parser.txt test_grammar.txt
) else if /i "%choice%"=="p" (
    echo Running Parser test...
    dotnet run -testP test_parser.txt test_grammar.txt
) else (
    echo Invalid choice. Please run the script again and choose either G or P.
)
pause