@echo off
echo Which test would you like to run?
echo (G)enerator
echo (P)arser
echo (B)oth
echo (W)riting
echo (R)eading
echo (F)ull Pipeline in production (Generator + Writing + Reading + Parser), outputs test_full_pipeline.cta

set ESC=
SET "mySeq=<   >%ESC%[1D%ESC%[1D%ESC%[1D%ESC%[1D"
choice /c GPBWRF /n /m "%mySeq%"
set choice=%errorlevel%
if %choice%==1 (
    echo Running Generator test...
    dotnet run -- test generator test_generator.txt
    if errorlevel 1 exit /b 1
) else if %choice%==2 (
    echo Running Parser test...
    dotnet run -- test parser test_parser.txt test_grammar.ct
    if errorlevel 1 exit /b 1
) else if %choice%==3 (
    echo Running both tests...
    dotnet run -- test generator test_generator.txt
    if errorlevel 1 exit /b 1
    dotnet run -- test parser test_parser.txt test_grammar.ct
    if errorlevel 1 exit /b 1
) else if %choice%==4 (
    echo Running Writing test...
    dotnet run -- build test_grammar.ct -o test_generated.at
    if errorlevel 1 exit /b 1
) else if %choice%==5 (
    echo Running Reading test...
    dotnet run -- parse test_generated.at test_code.txt
    if errorlevel 1 exit /b 1
) else if %choice%==6 (
    echo Running Full Pipeline test...
    dotnet run -- build test_grammar.ct --display > test_generated_raw.txt
    if errorlevel 1 exit /b 1
    dotnet run -- build test_grammar.ct -o test_generated.at
    if errorlevel 1 exit /b 1
    dotnet run -- parse test_generated.at test_code.txt > test_full_pipeline.cta
    if errorlevel 1 exit /b 1
) else if %choice%==0 (
    exit
)