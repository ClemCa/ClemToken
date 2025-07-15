@echo off
echo Which test would you like to run?
echo (G)enerator
echo (P)arser
echo (B)oth
echo (W)riting
echo (R)eading

set ESC=
SET "mySeq=<   >%ESC%[1D%ESC%[1D%ESC%[1D%ESC%[1D"
choice /c GPBWR /n /m "%mySeq%"
set choice=%errorlevel%
if %choice%==1 (
    echo Running Generator test...
    dotnet run -testG test_generator.txt -p:StartupObject=ClemToken.Program
) else if %choice%==2 (
    echo Running Parser test...
    dotnet run -testP test_parser.txt test_grammar.txt -p:StartupObject=ClemToken.Program
) else if %choice%==3 (
    echo Running both tests...
    dotnet run -testG test_generator.txt -p:StartupObject=ClemToken.Program
    dotnet run -testP test_parser.txt test_grammar.txt -p:StartupObject=ClemToken.Program
) else if %choice%==4 (
    echo Running Writing test...
    dotnet run -- -f test_grammar.txt -o test_generated.at -p:StartupObject=ClemToken.Program
) else if %choice%==5 (
    echo Running Reading test...
    dotnet run -- -f test_generated.at -i test_code.txt -p:StartupObject=ClemToken.Program
)else if %choice%==0 (
    exit
)