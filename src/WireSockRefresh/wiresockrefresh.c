#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <windows.h>
#include <direct.h>

#define SERVICE_NAME "wiresock-client-service"
#define LOG_FILE "wiresockrefresh.log"
#define REFRESH_INTERVAL 300 // 5 minutes

void writeLog(const char* message) {
    time_t now;
    struct tm *timeinfo;
    char timestamp[64];
    char logPath[MAX_PATH];
    FILE *logFile;
    
    // Get current timestamp
    time(&now);
    timeinfo = localtime(&now);
    strftime(timestamp, sizeof(timestamp), "%Y-%m-%d %H:%M:%S", timeinfo);
    
    // Get executable directory
    GetModuleFileName(NULL, logPath, MAX_PATH);
    char *lastSlash = strrchr(logPath, '\\');
    if (lastSlash) {
        *(lastSlash + 1) = '\0';
        strcat(logPath, LOG_FILE);
    } else {
        strcpy(logPath, LOG_FILE);
    }
    
    // Write to log file
    logFile = fopen(logPath, "a");
    if (logFile) {
        fprintf(logFile, "[%s] %s\n", timestamp, message);
        fclose(logFile);
    }
}

int stopService() {
    SC_HANDLE scManager, service;
    SERVICE_STATUS serviceStatus;
    int result = 0;
    
    // Open Service Control Manager
    scManager = OpenSCManager(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!scManager) {
        writeLog("Error: Could not open Service Control Manager");
        return 0;
    }
    
    // Open the service
    service = OpenService(scManager, SERVICE_NAME, SERVICE_STOP | SERVICE_QUERY_STATUS);
    if (!service) {
        writeLog("Error: Could not open service - service may not exist or insufficient privileges");
        CloseServiceHandle(scManager);
        return 0;
    }
    
    // Stop the service
    if (!ControlService(service, SERVICE_CONTROL_STOP, &serviceStatus)) {
        DWORD error = GetLastError();
        if (error == ERROR_SERVICE_NOT_ACTIVE) {
            writeLog("Service was already stopped");
            result = 1;
        } else {
            char errorMsg[256];
            sprintf(errorMsg, "Error stopping service: %lu", error);
            writeLog(errorMsg);
        }
    } else {
        writeLog("Service stop command sent successfully");
        result = 1;
    }
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    return result;
}

int startService() {
    SC_HANDLE scManager, service;
    int result = 0;
    
    // Open Service Control Manager
    scManager = OpenSCManager(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!scManager) {
        writeLog("Error: Could not open Service Control Manager");
        return 0;
    }
    
    // Open the service
    service = OpenService(scManager, SERVICE_NAME, SERVICE_START | SERVICE_QUERY_STATUS);
    if (!service) {
        writeLog("Error: Could not open service - service may not exist or insufficient privileges");
        CloseServiceHandle(scManager);
        return 0;
    }
    
    // Start the service
    if (!StartService(service, 0, NULL)) {
        DWORD error = GetLastError();
        if (error == ERROR_SERVICE_ALREADY_RUNNING) {
            writeLog("Service was already running");
            result = 1;
        } else {
            char errorMsg[256];
            sprintf(errorMsg, "Error starting service: %lu", error);
            writeLog(errorMsg);
        }
    } else {
        writeLog("Service start command sent successfully");
        result = 1;
    }
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    return result;
}

int restartService() {
    writeLog("Starting service restart process...");
    
    // Stop the service
    if (!stopService()) {
        writeLog("Failed to stop service - service may not exist or be accessible");
        return 0;
    }
    
    // Wait a moment for the service to stop
    Sleep(2000);
    
    // Start the service
    if (!startService()) {
        writeLog("Failed to start service - service may not exist or be accessible");
        return 0;
    }
    
    writeLog("Service restart completed successfully");
    return 1;
}

int main(int argc, char *argv[]) {
    time_t startTime, currentTime;
    int cycleCount = 0;
    int serviceMode = 0;
    
    // Check if running as service (no console window)
    if (argc > 1 && strcmp(argv[1], "service") == 0) {
        serviceMode = 1;
        // Hide console window when running as service
        ShowWindow(GetConsoleWindow(), SW_HIDE);
    }
    
    writeLog("WireSock Refresh Service started");
    writeLog("Service refresh interval: 5 minutes");
    writeLog("Target service: wiresock-client-service");
    if (serviceMode) {
        writeLog("Running in service mode (no console window)");
    } else {
        writeLog("Running in console mode");
    }
    
    startTime = time(NULL);
    
    while (1) {
        currentTime = time(NULL);
        
        // Check if 5 minutes have passed
        if (difftime(currentTime, startTime) >= REFRESH_INTERVAL) {
            cycleCount++;
            char cycleMsg[128];
            sprintf(cycleMsg, "Starting refresh cycle #%d", cycleCount);
            writeLog(cycleMsg);
            
            // Restart the service
            if (restartService()) {
                writeLog("Refresh cycle completed successfully");
            } else {
                writeLog("Refresh cycle failed - service may not exist or be accessible");
                writeLog("Will retry in 300 seconds");
            }
            
            // Reset timer regardless of success or failure
            startTime = time(NULL);
        }
        
        // Sleep for 1 second before checking again
        Sleep(1000);
    }
    
    return 0;
}
