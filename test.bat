@echo off
echo Which test would you like to run?
echo (G)enerator
echo (P)arser
echo (B)oth
echo (W)riting
echo (R)eading
echo (F)ull Pipeline in production (Generator + Writing + Reading + Parser), outputs test_full_pipeline.txt

set ESC=
SET "mySeq=<   >%ESC%[1D%ESC%[1D%ESC%[1D%ESC%[1D"
choice /c GPBWRF /n /m "%mySeq%"
set choice=%errorlevel%
if %choice%==1 (
    echo Running Generator test...
    dotnet run -testG test_generator.txt -p:StartupObject=ClemToken.Program
) else if %choice%==2 (
    echo Running Parser test...
    dotnet run -testP test_parser.txt test_grammar.ct -p:StartupObject=ClemToken.Program
) else if %choice%==3 (
    echo Running both tests...
    dotnet run -testG test_generator.txt -p:StartupObject=ClemToken.Program
    dotnet run -testP test_parser.txt test_grammar.ct -p:StartupObject=ClemToken.Program
) else if %choice%==4 (
    echo Running Writing test...
    dotnet run -p:StartupObject=ClemToken.Program -- -f test_grammar.ct -o test_generated.at
) else if %choice%==5 (
    echo Running Reading test...
    dotnet run -p:StartupObject=ClemToken.Program -- -f test_generated.at -i test_code.txt
) else if %choice%==6 (
    echo Running Full Pipeline test...
    dotnet run -p:StartupObject=ClemToken.Program -- -f test_grammar.ct -o test_generated.at
    dotnet run -p:StartupObject=ClemToken.Program -- -f test_generated.at -i test_code.txt > test_full_pipeline.cta
) else if %choice%==0 (
    exit
)