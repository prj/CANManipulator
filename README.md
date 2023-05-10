# CANManipulator

## Description
CAN interceptor/logger/modifier for use with two J2534 devices

## Background
This was written to force SVM rear camera retrofit to succeed using ODIS on an Audi A4 B9 MY17 where modules 17 and 5F were flashed to newer variants using an SD card.

## Hardware requirements
2x J2534 devices.   
If you use the same ones, then you need to modify the code, to select two different devices, otherwise just edit the DLL location.

A CAN Y cable to connect ODIS to the second J2534 device. The Y cable also needs:   
Pin 1 - Kl. 15   
Pin 16 - Kl. 30   
Pin 4 - GND   
Pin 5 - GND   
Pin 6 & 14 should have a 60 ohm resistor between them

## Software requirements
You will need to add a reference to this project:   
https://github.com/BrianHumlicek/J2534-Sharp
