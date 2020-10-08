
# OxiMeter
This is a project to measure bloods oxygen level using a Max30102 controlled by an Arduino and showing the result (including SpO2, Heart Beat Rate and Heart Pulses) on an android phone (using USB-otg)

## Hardware

1- Arduino (Uno - Micro or any other Arduino with USB port)

2- Max30102 Pulse Oximeter

3- an android phone or tablet with USB-otg capability

Connect the Max30102 to Arduino using I2C bus, the software also depend on interrupts from the max chipset to know when the new data available so connect the interrupt pin on Max to pin A3 on Arduino (you can use other pins but you should change the program -line 45 /Arduino_Max30102/Arduino_Max30102.ino)

## Software
Program your Arduino using the Arduino IDE. To build the android software you will need Visual Studio with  Xamarin.Android feature installed. open the solution (Oximeter.sln) and deploy it to your android device, disconnect the phone from PC and connect it to the Arduino and press the start.

For more convenient I have compiled the project and uploaded it as a rar file: **com.simorgh.oximeter.rar** it doesn't need any permission exempt reading of USB-otg

## Notice
1-Some max30102 chipsets save the readings of IR and RED channels to the buffer, in revers. In that case the Spo2 level would be faulty (it would probably show -999). To correct the reading change the lines 220-225 of Oximeter/MainActivity.cs

2-Some Chinese max30102 breakout board are faulty and designed incorrectly for further information on that check the following link:
https://reedpaper.wordpress.com/2018/08/22/pulse-oximeter-max30100-max30102-how-to-fix-wrong-board/

## Acknowledgment
I have used [aromring](https://github.com/aromring/MAX30102_by_RF)'s algorithm to calculate the SpO2 and Heart rate in C#, The Arduino also use his code to setup the Max30102

The USB-serial driver for Xamarin.Android is from [officialdoniald](https://github.com/officialdoniald/Xamarin.Android.SerialPort)

And Finally the application's icon is from [MedicalWP](https://iconarchive.com/show/medical-icons-by-medicalwp/Cardiology-red-icon.html)
 

