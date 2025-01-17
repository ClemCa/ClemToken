@echo off
echo Which test would you like to run?
echo (A)ll
echo (G)enerator
echo (P)arser

set ESC=
SET "mySeq=<   >%ESC%[1D%ESC%[1D%ESC%[1D%ESC%[1D"
choice /c GPA /n /m "%mySeq%"
set choice=%errorlevel%
if %choice%==1 (
    echo Running Generator test...
    dotnet run -testG test_generator.txt
) else if %choice%==2 (
    echo Running Parser test...
    dotnet run -testP test_parser.txt test_grammar.txt
) else if %choice%==3 (
    echo Running all tests...
    dotnet run -testG test_generator.txt
    dotnet run -testP test_parser.txt test_grammar.txt
) else if %choice%==0 (
    exit
)