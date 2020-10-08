/********************************************************
*
* Project: MAXREFDES117#
* Filename: RD117_ARDUINO.ino
* Description: This module contains the Main application for the MAXREFDES117 example program.
*
* Revision History:
*\n 1-18-2016 Rev 01.00 GL Initial release.
*\n 12-22-2017 Rev 02.00 Significantlly modified by Robert Fraczkiewicz
*\n 08-22-2018 Rev 02.01 Added conditional compilation of the code related to ADALOGGER SD card operations
*
* --------------------------------------------------------------------
*
* This code follows the following naming conventions:
*
* char              ch_pmod_value
* char (array)      s_pmod_s_string[16]
* float             f_pmod_value
* int32_t           n_pmod_value
* int32_t (array)   an_pmod_value[16]
* int16_t           w_pmod_value
* int16_t (array)   aw_pmod_value[16]
* uint16_t          uw_pmod_value
* uint16_t (array)  auw_pmod_value[16]
* uint8_t           uch_pmod_value
* uint8_t (array)   auch_pmod_buffer[16]
* uint32_t          un_pmod_value
* int32_t *         pn_pmod_value
*
* ------------------------------------------------------------------------- */

#include <Arduino.h>
#include <Wire.h>


#include "max30102.h"


uint32_t elapsedTime,timeStart;

uint32_t aun_ir_buffer[2]; //infrared LED sensor data
uint32_t aun_red_buffer[2];  //red LED sensor data

uint8_t uch_dummy;
const byte oxiInt = A3;
uint32_t smpln;
String chkPnt;

void setup() {

  Wire.begin();

  /*
  maxSensor.begin(Wire, I2C_SPEED_FAST);
  byte ledBrightness = 0x24; //Options: 0=Off to 255=50mA
  byte sampleAverage = 4; //Options: 1, 2, 4, 8, 16, 32
  byte ledMode = 2; //Options: 1 = Red only, 2 = Red + IR, 3 = Red + IR + Green
  int sampleRate = 100; //Options: 50, 100, 200, 400, 800, 1000, 1600, 3200
  int pulseWidth = 411; //Options: 69, 118, 215, 411
  int adcRange = 4096; //Options: 2048, 4096, 8192, 16384

  maxSensor.setup(ledBrightness, sampleAverage, ledMode, sampleRate, pulseWidth, adcRange);
  */

  // initialize serial communication at 115200 bits per second:
  Serial.begin(115200);
  pinMode(oxiInt, INPUT);
   
  maxim_max30102_reset(); //resets the MAX30102
  delay(3000);

  maxim_max30102_read_reg(REG_INTR_STATUS_1,&uch_dummy);  //Reads/clears the interrupt status register
  maxim_max30102_init();  //initialize the MAX30102

  maxim_max30102_read_reg(0xFF,&uch_dummy);
  
  while(Serial.available()==0)  //wait until user presses a key
  {
    delay(1000);
  }
  delay(1000);
  chkPnt=Serial.readString();
    
  timeStart=millis();
}

//Continuously taking samples from MAX30102.  Heart rate and SpO2 are calculated every ST seconds
void loop() {

    while(analogRead(oxiInt)>750);
    maxim_max30102_read_fifo((aun_red_buffer+1), (aun_ir_buffer+1));  //read from MAX30102 FIFO
    Serial.print(chkPnt);
    Serial.print(",");
    Serial.print(smpln, DEC);
    Serial.print(",");
    Serial.print(millis()-timeStart, DEC);
    Serial.print(",");
    Serial.print(aun_red_buffer[1], DEC);
    Serial.print(",");
    Serial.print(aun_ir_buffer[1], DEC);    
    Serial.print(";");
    smpln=smpln+1;
    
}
