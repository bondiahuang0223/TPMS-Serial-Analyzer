# TPMS-Serial-Analyzer

A C# WPF application for real-time TPMS BLE sensor data 
visualization, analysis, and logging via serial (UART) output 
from a BLE dongle.

Developed during firmware validation for an automotive TPMS 
project targeting Tier-1 client (Kenda Rubber), using 
Infineon SP49 + Silicon Labs BG22 based sensors.

## Features

- Real-time parsing of BLE dongle UART data stream
- Dynamic device cards displaying per-sensor data:
  - Tire pressure (kPa)
  - Temperature (°C)
  - Mileage (km)
  - Revolution & Footprint (μs)
  - Battery voltage with low-voltage warning indicator
  - MAC address and timestamp
- Supports up to 20 simultaneous sensor devices
- Optional timestamp display toggle
- Background CSV buffering (up to 5MB) with overflow warning
- One-click CSV export with auto-generated filename
- MVVM architecture with event-driven design
- MaterialDesign UI

## Tech Stack

- Language: C# (.NET / WPF)
- Architecture: MVVM (INotifyPropertyChanged, ObservableCollection,
  RelayCommand)
- UI: MaterialDesignThemes
- Data parsing: Regex-based UART frame parser
- Serial communication: System.IO.Ports.SerialPort
- Supported baud rates: 9600 ~ 921600 (default: 256000 for TPMS)

## Background

This tool was built to support real-vehicle road tests and 
dynamic data acquisition during TPMS firmware validation.
The BLE dongle receives sensor advertisements and outputs 
parsed data via UART; this application captures, parses, 
visualizes, and logs the data in real time.

Sensor data format supported:
- Format 1: Pressure / Temperature / Voltage / Mileage
- Format 2: Revolution / Footprint

## Screenshot
<img width="1552" height="775" alt="image" src="https://github.com/user-attachments/assets/60e7c66f-624c-41eb-ada4-aeeeac4f1d90" />
