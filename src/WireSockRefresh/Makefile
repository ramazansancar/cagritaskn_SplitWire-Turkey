CC = gcc
CFLAGS = -Wall -Wextra -std=c99
LIBS = -ladvapi32

TARGET = wiresockrefresh.exe
SOURCE = wiresockrefresh.c

$(TARGET): $(SOURCE)
	$(CC) $(CFLAGS) -o $(TARGET) $(SOURCE) $(LIBS)

clean:
	del $(TARGET) 2>nul || true

.PHONY: clean
