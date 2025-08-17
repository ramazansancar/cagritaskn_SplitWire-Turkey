@echo off
echo ========================================
echo WireSock Refresh Service Builder
echo ========================================
echo.

REM Add MinGW to PATH if not already there
set "MINGW_PATH=C:\msys64\mingw64\bin"
if not "%PATH%" == "%PATH%;%MINGW_PATH%" (
    set "PATH=%MINGW_PATH%;%PATH%"
)

REM Check if GCC is available
gcc --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: GCC compiler not found!
    echo.
    echo Trying to find MinGW in common locations...
    
    REM Try different possible MinGW paths
    set "PATHS_TO_TRY=C:\msys64\mingw64\bin;C:\mingw64\bin;C:\mingw\bin;C:\MinGW\bin"
    
    for %%p in (%PATHS_TO_TRY%) do (
        if exist "%%p\gcc.exe" (
            echo Found GCC in: %%p
            set "PATH=%%p;%PATH%"
            goto :found_gcc
        )
    )
    
    echo.
    echo GCC not found in common locations.
    echo Please make sure MinGW is installed and try again.
    echo.
    echo You can install MinGW using MSYS2:
    echo 1. Download MSYS2 from https://www.msys2.org/
    echo 2. Install it
    echo 3. Run: pacman -S mingw-w64-x86_64-gcc mingw-w64-x86_64-make
    echo.
    pause
    exit /b 1
    
    :found_gcc
)

echo GCC compiler found. Building application...
echo.

REM Clean previous build
if exist wiresockrefresh.exe (
    echo Removing previous build...
    del wiresockrefresh.exe
)

REM Build the application
echo Building wiresockrefresh.exe...
gcc -Wall -Wextra -std=c99 -o wiresockrefresh.exe wiresockrefresh.c -ladvapi32

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo BUILD SUCCESSFUL!
    echo ========================================
    echo.
    echo wiresockrefresh.exe has been created successfully.
    echo.
    echo To run the application (requires Administrator privileges):
    echo   wiresockrefresh.exe
    echo.
    echo The application will:
    echo - Restart wiresock-client-service every 5 minutes
    echo - Log all activities to wiresockrefresh.log
    echo.
    echo To run in service mode (no console window):
    echo   wiresockrefresh.exe service
    echo.
    echo To install as a Windows service:
    echo   sc create WireSockRefresh binPath= "%~dp0wiresockrefresh.exe service" start= auto
    echo   sc start WireSockRefresh
    echo.
) else (
    echo.
    echo ========================================
    echo BUILD FAILED!
    echo ========================================
    echo.
    echo There was an error during compilation.
    echo Please check the error messages above.
    echo.
)

pause
