using System;
using System.Collections.Generic;
//using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Usb;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Util;
using Android.Views;
using Android.Widget;
using Hoho.Android.UsbSerial.Driver;
using Hoho.Android.UsbSerial.Extensions;
using Hoho.Android.UsbSerial.Util;
using static Java.Interop.JniEnvironment;

namespace OxiMeter
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, ScreenOrientation = ScreenOrientation.Landscape)]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
    [MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
    // app icon:
    // https://iconarchive.com/show/medical-health-icons-by-graphicloads/lungs-icon.html

    public class MainActivity : AppCompatActivity
    {
        const string ACTION_USB_PERMISSION = "com.hoho.android.usbserial.examples.USB_PERMISSION";
        UsbManager usbManager;
        UsbSerialPortAdapter adapter;
        BroadcastReceiver detachedReceiver;
        BroadcastReceiver attachedReceiver;
        UsbSerialPort selectedPort, port;
        SerialInputOutputManager serialIoManager;


        struct dataFormat
        {
            public long irData;
            public long rdData;
            public long tm;
            public long dtcnt;
        }
        string rcvdTxt = "";
        List<dataFormat> algoritmData = new List<dataFormat>();
        List<long> plotData = new List<long>();
        int newDataCnt = 0;
        string chkPnt;

        TextView txtHeartRate, txtOxiMeter;
        Button btnStart;
        ImageView imgPlotter;
        //RelativeLayout rltvLayout;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            usbManager = GetSystemService(Context.UsbService) as UsbManager;

            txtHeartRate = FindViewById<TextView>(Resource.Id.txtHeartRate);
            txtOxiMeter = FindViewById<TextView>(Resource.Id.txtOxiValue);
            btnStart = FindViewById<Button>(Resource.Id.btnStart);
            //btnClear = FindViewById<Button>(Resource.Id.btnClear);
            imgPlotter = FindViewById<ImageView>(Resource.Id.imgPlot);
            //rltvLayout = FindViewById<RelativeLayout>(Resource.Id.rltvLayout);

            btnStart.Click += btnStart_Click;


        }

        protected override void OnResume()
        {
            base.OnResume();

            adapter = new UsbSerialPortAdapter(this);
            PopulateList();
            detachedReceiver = new UsbDeviceDetachedReceiver(this);
            RegisterReceiver(detachedReceiver, new IntentFilter(UsbManager.ActionUsbDeviceDetached));
            attachedReceiver = new UsbDeviceAttachedReceiver(this);
            RegisterReceiver(detachedReceiver, new IntentFilter(UsbManager.ActionUsbDeviceAttached));
        }

        public async void btnStart_Click(object sender, System.EventArgs e)
        {
            plotter.init(imgPlotter.Width, imgPlotter.Height);
            #region Setup Serial port
            selectedPort = adapter.GetItem(0);
            var permissionGranted = await usbManager.RequestPermissionAsync(selectedPort.Driver.Device, this);
            if (!permissionGranted)
            {
                //Permission Denied
                return;
            }
            var portInfo = new UsbSerialPortInfo(selectedPort);
            int vendorId = portInfo.VendorId;
            int deviceId = portInfo.DeviceId;
            int portNumber = portInfo.PortNumber;

            var drivers = FindAllDrivers(usbManager);
            var driver = drivers.Where((d) => d.Device.VendorId == vendorId && d.Device.DeviceId == deviceId).FirstOrDefault();
            if (driver == null)
                throw new Exception("Driver specified in extra tag not found.");

            port = driver.Ports[portNumber];
            if (port == null)
            {
                //No serial device
                return;
            }


            serialIoManager = new SerialInputOutputManager(port)
            {
                BaudRate = 115200,
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
            };
            serialIoManager.DataReceived += (sender, e) =>
            {
                RunOnUiThread(() =>
                {
                    ReceiveData(e.Data);
                });
            };
            try
            {
                serialIoManager.Open(usbManager);
            }
            catch (Java.IO.IOException)
            {
                //Error opening device
                return;
            }
            #endregion
            chkPnt = RandomString();
            WriteData(System.Text.Encoding.ASCII.GetBytes(chkPnt));

            btnStart.Enabled = false;
        }



        void ReceiveData(byte[] data)
        {
            string message = System.Text.Encoding.ASCII.GetString(data);
            rcvdTxt = rcvdTxt + message;
            if (rcvdTxt.Length < 100) return;

            dataFormat[] pulledData = splitter(ref rcvdTxt);

            newDataCnt = newDataCnt + pulledData.Count();
            foreach (dataFormat fld in pulledData)
            {
                plotData.Add(fld.irData);
                if (plotData.Count >= plotter.neededData()) plotData.RemoveAt(0);
            }
            if (newDataCnt > algoritm.sampleRate)
            {
                newDataCnt = 0;
                Bitmap bmp = plotter.plot(plotData.ToArray());
                imgPlotter.SetImageBitmap(bmp);
            }

            algoritmData.AddRange(pulledData);
            if (algoritmData.Count >= algoritm.bfSize)
            {
                long[] irData = new long[algoritm.bfSize];
                long[] rdData = new long[algoritm.bfSize];
                for (int i = 0; i < algoritm.bfSize; i++)
                {
                    dataFormat dt;
                    dt = algoritmData[i];
                    irData[i] = dt.rdData;
                    rdData[i] = dt.irData;
                }

                for (int i = 0; i < algoritm.sampleRate; i++) algoritmData.RemoveAt(0);

                algoritm.OxiValues OxValue = algoritm.getOxi(rdData, irData);

                txtHeartRate.Text = "Bpm: " + OxValue.HrtRate.ToString();
                txtOxiMeter.Text = "Oxi: " + OxValue.OxiLevel.ToString();
            }
        }

        private dataFormat[] splitter(ref String txt)
        {

            List<dataFormat> pulledData = new List<dataFormat>();
            int k = txt.LastIndexOf(";");

            String workingTxt = txt.Substring(0, k + 1);
            txt = txt.Substring(k + 1);

            String[] sbstrs = workingTxt.Split(";");

            foreach (String sbFld in sbstrs)
            {
                String[] fld = sbFld.Split(",");
                if (fld.Length == 5)
                {
                    dataFormat extrct;
                    try
                    {
                        //if (chkPnt == fld[0])
                        //{
                            extrct.dtcnt = long.Parse(fld[1]);
                            extrct.tm = long.Parse(fld[2]);

                            extrct.rdData = long.Parse(fld[3]);
                            extrct.irData = long.Parse(fld[4]);
                            // In Some Max30102 Censors, the red and Infrared readings are swaped
                            // in that case uncomment the fowlowing line and comment two lines befoer this comment
                            //extrct.rdData = long.Parse(fld[4]);
                            //extrct.irData = long.Parse(fld[3]);

                            pulledData.Add(extrct);
                        //}

                    }
                    catch { }

                }
            }
            return pulledData.ToArray();
        }

        private String RandomString()
        {
            var rand = new Random();
            var bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[i] = (byte)rand.Next(97, 122);
            }
            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        void WriteData(byte[] data)
        {
            int WRITE_WAIT_MILLIS = 1000;
            try
            {
                if (serialIoManager.IsOpen)
                {
                    port.Write(data, WRITE_WAIT_MILLIS);
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        internal static IList<IUsbSerialDriver> FindAllDrivers(UsbManager usbManager)
        {
            // using the default probe table
            // return UsbSerialProber.DefaultProber.FindAllDriversAsync (usbManager);

            // adding a custom driver to the default probe table
            var table = UsbSerialProber.DefaultProbeTable;
            table.AddProduct(0x1b4f, 0x0008, typeof(CdcAcmSerialDriver)); // IOIO OTG

            table.AddProduct(0x09D8, 0x0420, typeof(CdcAcmSerialDriver)); // Elatec TWN4


            UsbSerialProber prober = new UsbSerialProber(table);
            return prober.FindAllDrivers(usbManager);
        }

        public void PopulateList()
        {


            //Log.Info(TAG, "Refreshing device list ...");

            var drivers = FindAllDrivers(usbManager);

            adapter.Clear();
            foreach (var driver in drivers)
            {
                var ports = driver.Ports;
                //Log.Info(TAG, string.Format("+ {0}: {1} port{2}", driver, ports.Count, ports.Count == 1 ? string.Empty : "s"));
                foreach (var port in ports)
                    adapter.Add(port);
            }

            adapter.NotifyDataSetChanged();
            //progressBarTitle.Text = string.Format("{0} device{1} found", adapter.Count, adapter.Count == 1 ? string.Empty : "s");

            //Log.Info(TAG, "Done refreshing, " + adapter.Count + " entries found.");
        }

    }


    class UsbSerialPortAdapter : ArrayAdapter<UsbSerialPort>
    {
        public UsbSerialPortAdapter(Context context)
            : base(context, global::Android.Resource.Layout.SimpleExpandableListItem2)
        {
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var row = convertView;
            if (row == null)
            {
                var inflater = Context.GetSystemService(Context.LayoutInflaterService) as LayoutInflater;
                row = inflater.Inflate(global::Android.Resource.Layout.SimpleListItem2, null);
            }

            var port = this.GetItem(position);
            var driver = port.GetDriver();
            var device = driver.GetDevice();

            var title = string.Format("Vendor {0} Product {1}",
                HexDump.ToHexString((short)device.VendorId),
                HexDump.ToHexString((short)device.ProductId));
            row.FindViewById<TextView>(global::Android.Resource.Id.Text1).Text = title;

            var subtitle = device.Class.SimpleName;
            row.FindViewById<TextView>(global::Android.Resource.Id.Text2).Text = subtitle;

            return row;
        }
    }

    class UsbDeviceDetachedReceiver
        : BroadcastReceiver
    {
        readonly string TAG = typeof(UsbDeviceDetachedReceiver).Name;
        readonly MainActivity activity;

        public UsbDeviceDetachedReceiver(MainActivity activity)
        {
            this.activity = activity;
        }

        public override void OnReceive(Context context, Intent intent)
        {
            var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;

            Log.Info(TAG, "USB device detached: " + device.DeviceName);

            activity.PopulateList();
        }
    }

    class UsbDeviceAttachedReceiver
        : BroadcastReceiver
    {
        readonly string TAG = typeof(UsbDeviceAttachedReceiver).Name;
        readonly MainActivity activity;

        public UsbDeviceAttachedReceiver(MainActivity activity)
        {
            this.activity = activity;
        }

        public override void OnReceive(Context context, Intent intent)
        {
            var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;

            Log.Info(TAG, "USB device attached: " + device.DeviceName);

            activity.PopulateList();
        }
    }

    static class algoritm
    {
        public static int sampleRate = 25;    //sampling frequency
        private static int nlpint = sampleRate; // Initialize it to 25, which corresponds to heart rate of 60 bps, RF
        public static int bfSize = 100;

        public struct OxiValues
        {
            public int OxiLevel;
            public int HrtRate;
        }
        public static OxiValues getOxi(long[] rdData, long[] irData)
        {
            OxiValues rtvalue;
            maxim_heart_rate_and_oxygen_saturation_ res;
            res = maxim_heart_rate_and_oxygen_saturation(irData, bfSize,
                        rdData, 0, 0,
                        0, 0, nlpint);
            rtvalue.HrtRate = res.pn_heart_rate;
            rtvalue.OxiLevel = (int)res.pn_spo2;
            return rtvalue;
        }


        private struct maxim_heart_rate_and_oxygen_saturation_
        {
            public long[] pun_ir_buffer; public int n_ir_buffer_length;
            public long[] pun_red_buffer; public double pn_spo2; public int pch_spo2_valid;
            public int pn_heart_rate; public int pch_hr_valid; public int n_last_peak_interval;
        }
        private static maxim_heart_rate_and_oxygen_saturation_ maxim_heart_rate_and_oxygen_saturation(long[] pun_ir_buffer, int n_ir_buffer_length,
                                                                                           long[] pun_red_buffer, double pn_spo2, int pch_spo2_valid,
                                                                                              int pn_heart_rate, int pch_hr_valid, int n_last_peak_interval)
        {
            /*
              \brief        Calculate the heart rate and SpO2 level
              \par          Details
                            By detecting  peaks of PPG cycle and corresponding AC/DC of red/infra-red signal, the an_ratio for the SPO2 is computed.
                            Since this algorithm is aiming for Arm M0/M3. formaula for SPO2 did not achieve the accuracy due to register overflow.
                            Thus, accurate SPO2 is precalculated and save longo uch_spo2_table[] per each an_ratio.

              \param[in]    *pun_ir_buffer           - IR sensor data buffer
              \param[in]    n_ir_buffer_length      - IR sensor data buffer length
              \param[in]    *pun_red_buffer          - Red sensor data buffer
              \param[out]    *pn_spo2                - Calculated SpO2 value
              \param[out]    *pch_spo2_valid         - 1 if the calculated SpO2 value is valid
              \param[out]    *pn_heart_rate          - Calculated heart rate value
              \param[out]    *pch_hr_valid           - 1 if the calculated heart rate value is valid

              \retval       None
             */
            maxim_heart_rate_and_oxygen_saturation_ rtValue = new maxim_heart_rate_and_oxygen_saturation_();

            int BUFFER_SIZE = sampleRate * 4;
            int MA4_SIZE = 4; // DONOT CHANGE
            int BUFFER_SIZE_MA4 = BUFFER_SIZE - MA4_SIZE;

            //long un_only_once;
            long un_ir_mean;
            int k, n_i_ratio_count;
            //int s, m;
            int i, n_exact_ir_valley_locs_count, n_middle_idx;
            long n_th1;
            // int n_c_min;
            int n_npks = 0;
            long[] an_ir_valley_locs = new long[15];
            long n_peak_interval_sum;


            long n_y_ac;
            long n_x_ac;
            //  int32_t n_spo2_calc;
            long n_y_dc_max;
            long n_x_dc_max;
            int n_y_dc_max_idx = 0, n_x_dc_max_idx = 0;
            long[] an_ratio = new long[5];
            long n_ratio_average;
            long n_nume;
            long n_denom;
            long[] an_x = new long[BUFFER_SIZE]; //ir
            long[] an_y = new long[BUFFER_SIZE]; //red

            double[] uch_spo2_table =
            {94.845,95.144034,95.434056,95.715066,95.987064,96.25005,96.504024,96.748986,96.984936,97.211874,97.4298,97.638714,97.838616,98.029506,
                98.211384,98.38425,98.548104,98.702946,98.848776,98.985594,99.1134,99.232194,99.341976,99.442746,99.534504,99.61725,99.690984,99.755706,
                99.811416,99.858114,99.8958,99.924474,99.944136,99.954786,99.956424,99.94905,99.932664,99.907266,99.872856,99.829434,99.777,99.715554,
                99.645096,99.565626,99.477144,99.37965,99.273144,99.157626,99.033096,98.899554,98.757,98.605434,98.444856,98.275266,98.096664,97.90905,
                97.712424,97.506786,97.292136,97.068474,96.8358,96.594114,96.343416,96.083706,95.814984,95.53725,95.250504,94.954746,94.649976,94.336194,
                94.0134,93.681594,93.340776,92.990946,92.632104,92.26425,91.887384,91.501506,91.106616,90.702714,90.2898,89.867874,89.436936,88.996986,
                88.548024,88.09005,87.623064,87.147066,86.662056,86.168034,85.665,85.152954,84.631896,84.101826,83.562744,83.01465,82.457544,81.891426,
                81.316296,80.732154,80.139,79.536834,78.925656,78.305466,77.676264,77.03805,76.390824,75.734586,75.069336,74.395074,73.7118,73.019514,
                72.318216,71.607906,70.888584,70.16025,69.422904,68.676546,67.921176,67.156794,66.3834,65.600994,64.809576,64.009146,63.199704,62.38125,
                61.553784,60.717306,59.871816,59.017314,58.1538,57.281274,56.399736,55.509186,54.609624,53.70105,52.783464,51.856866,50.921256,49.976634,
                49.023,48.060354,47.088696,46.108026,45.118344,44.11965,43.111944,42.095226,41.069496,40.034754,38.991,37.938234,36.876456,35.805666,
                34.725864,33.63705,32.539224,31.432386,30.316536,29.191674,28.0578,26.914914,25.763016,24.602106,23.432184,22.25325,21.065304,19.868346,
                18.662376,17.447394,16.2234,14.990394,13.748376,12.497346,11.237304,9.96825,8.690184,7.403106,6.107016,4.801914,3.4878,2.164674,0.832536,
                0.0};

            // calculates DC mean and subtracts DC from ir
            un_ir_mean = 0;
            for (k = 0; k < n_ir_buffer_length; k++) un_ir_mean += pun_ir_buffer[k];
            un_ir_mean = un_ir_mean / n_ir_buffer_length;

            // remove DC and invert signal so that we can use peak detector as valley detector
            for (k = 0; k < n_ir_buffer_length; k++)
                an_x[k] = un_ir_mean - pun_ir_buffer[k];

            // 4 pt Moving Average
            for (k = 0; k < BUFFER_SIZE_MA4; k++)
            {
                an_x[k] = (an_x[k] + an_x[k + 1] + an_x[k + 2] + an_x[k + 3]) / (int)4;
            }
            // calculate threshold
            n_th1 = 0;
            for (k = 0; k < BUFFER_SIZE_MA4; k++)
            {
                n_th1 += an_x[k];
            }
            n_th1 = n_th1 / (BUFFER_SIZE_MA4);
            if (n_th1 < 30) n_th1 = 30; // min allowed
            if (n_th1 > 60) n_th1 = 60; // max allowed

            for (k = 0; k < 15; k++) an_ir_valley_locs[k] = 0;
            // since we flipped signal, we use peak detector as valley detector
            maxim_find_peaks_ res3 = maxim_find_peaks(an_ir_valley_locs, n_npks, an_x, BUFFER_SIZE_MA4, n_th1, 4, 15);//peak_height, peak_distance, max_num_peaks

            an_ir_valley_locs = res3.pn_locs;
            n_npks = res3.n_npks;
            an_x = res3.pn_x;
            BUFFER_SIZE_MA4 = res3.n_size;
            n_th1 = res3.n_min_height;

            n_peak_interval_sum = 0;
            if (n_npks >= 2)
            {
                for (k = 1; k < n_npks; k++) n_peak_interval_sum += (an_ir_valley_locs[k] - an_ir_valley_locs[k - 1]);
                n_peak_interval_sum = n_peak_interval_sum / (n_npks - 1);
                pn_heart_rate = (int)((sampleRate * 60) / n_peak_interval_sum);
                pch_hr_valid = 1;
            }
            else
            {
                pn_heart_rate = -999; // unable to calculate because # of peaks are too small
                pch_hr_valid = 0;
            }

            //  load raw value again for SPO2 calculation : RED(=y) and IR(=X)
            for (k = 0; k < n_ir_buffer_length; k++)
            {
                an_x[k] = pun_ir_buffer[k];
                an_y[k] = pun_red_buffer[k];
            }

            // find precise min near an_ir_valley_locs
            n_exact_ir_valley_locs_count = n_npks;

            //using exact_ir_valley_locs , find ir-red DC and ir-red AC for SPO2 calibration an_ratio
            //finding AC/DC maximum of raw

            n_ratio_average = 0;
            n_i_ratio_count = 0;
            for (k = 0; k < 5; k++) an_ratio[k] = 0;
            for (k = 0; k < n_exact_ir_valley_locs_count; k++)
            {
                if (an_ir_valley_locs[k] > BUFFER_SIZE)
                {
                    pn_spo2 = -999; // do not use SPO2 since valley loc is out of range
                    pch_spo2_valid = 0;
                    rtValue.n_ir_buffer_length = n_ir_buffer_length;
                    rtValue.n_last_peak_interval = n_last_peak_interval;
                    rtValue.pch_hr_valid = pch_hr_valid;
                    rtValue.pch_spo2_valid = pch_spo2_valid;
                    rtValue.pn_heart_rate = pn_heart_rate;
                    rtValue.pn_spo2 = pn_spo2;
                    rtValue.pun_ir_buffer = pun_ir_buffer;
                    rtValue.pun_red_buffer = pun_red_buffer;
                    return rtValue;
                }
            }
            // find max between two valley locations
            // and use an_ratio betwen AC compoent of Ir & Red and DC compoent of Ir & Red for SPO2
            for (k = 0; k < n_exact_ir_valley_locs_count - 1; k++)
            {
                n_y_dc_max = -16777216;
                n_x_dc_max = -16777216;
                if (an_ir_valley_locs[k + 1] - an_ir_valley_locs[k] > 3)
                {
                    for (i = (int)an_ir_valley_locs[k]; i < an_ir_valley_locs[k + 1]; i++)
                    {
                        if (an_x[i] > n_x_dc_max) { n_x_dc_max = an_x[i]; n_x_dc_max_idx = i; }
                        if (an_y[i] > n_y_dc_max) { n_y_dc_max = an_y[i]; n_y_dc_max_idx = i; }
                    }
                    n_y_ac = (an_y[(int)an_ir_valley_locs[k + 1]] - an_y[(int)an_ir_valley_locs[k]]) * (n_y_dc_max_idx - an_ir_valley_locs[k]); //red
                    n_y_ac = an_y[(int)an_ir_valley_locs[k]] + n_y_ac / (an_ir_valley_locs[k + 1] - an_ir_valley_locs[k]);
                    n_y_ac = an_y[n_y_dc_max_idx] - n_y_ac;    // subracting linear DC compoenents from raw
                    n_x_ac = (an_x[(int)an_ir_valley_locs[k + 1]] - an_x[(int)an_ir_valley_locs[k]]) * (n_x_dc_max_idx - an_ir_valley_locs[k]); // ir
                    n_x_ac = an_x[(int)an_ir_valley_locs[k]] + n_x_ac / (an_ir_valley_locs[k + 1] - an_ir_valley_locs[k]);
                    n_x_ac = an_x[n_y_dc_max_idx] - n_x_ac;      // subracting linear DC compoenents from raw
                    n_nume = (n_y_ac * n_x_dc_max) >> 7; //prepare X100 to preserve floating value
                    n_denom = (n_x_ac * n_y_dc_max) >> 7;
                    if (n_denom > 0 && n_i_ratio_count < 5 && n_nume != 0)
                    {
                        an_ratio[n_i_ratio_count] = (n_nume * 100) / n_denom; //formular is ( n_y_ac *n_x_dc_max) / ( n_x_ac *n_y_dc_max) ;
                        n_i_ratio_count++;
                    }
                }
            }
            // choose median value since PPG signal may varies from beat to beat

            maxim_sort_ascend_ res = maxim_sort_ascend(an_ratio, n_i_ratio_count);
            an_ratio = res.pn_x;
            n_i_ratio_count = res.n_size;

            n_middle_idx = n_i_ratio_count / 2;

            if (n_middle_idx > 1)
                n_ratio_average = (an_ratio[n_middle_idx - 1] + an_ratio[n_middle_idx]) / 2; // use median
            else
                n_ratio_average = an_ratio[n_middle_idx];

            if (n_ratio_average > 2 && n_ratio_average < 184)
            {
                //    n_spo2_calc= uch_spo2_table[n_ratio_average] ;
                pn_spo2 = uch_spo2_table[n_ratio_average];
                pch_spo2_valid = 1;//  float_SPO2 =  -45.060*n_ratio_average* n_ratio_average/10000 + 30.354 *n_ratio_average/100 + 94.845 ;  // for comparison with table
            }
            else
            {
                pn_spo2 = -999; // do not use SPO2 since signal an_ratio is out of range
                pch_spo2_valid = 0;
            }

            rtValue.n_ir_buffer_length = n_ir_buffer_length;
            rtValue.n_last_peak_interval = n_last_peak_interval;
            rtValue.pch_hr_valid = pch_hr_valid;
            rtValue.pch_spo2_valid = pch_spo2_valid;
            rtValue.pn_heart_rate = pn_heart_rate;
            rtValue.pn_spo2 = pn_spo2;
            rtValue.pun_ir_buffer = pun_ir_buffer;
            rtValue.pun_red_buffer = pun_red_buffer;
            return rtValue;
        }
        private struct maxim_find_peaks_ { public long[] pn_locs; public int n_npks; public long[] pn_x; public int n_size; public long n_min_height; public int n_min_distance; public int n_max_num; }
        private static maxim_find_peaks_ maxim_find_peaks(long[] pn_locs, int n_npks, long[] pn_x, int n_size, long n_min_height, int n_min_distance, int n_max_num)
        {
            /*
             \brief        Find peaks
             \par          Details
                           Find at most MAX_NUM peaks above MIN_HEIGHT separated by at least MIN_DISTANCE

             \retval       None
            */
            maxim_find_peaks_ rtValues = new maxim_find_peaks_();
            maxim_peaks_above_min_height_ res2 = maxim_peaks_above_min_height(pn_locs, n_npks, pn_x, n_size, n_min_height);
            pn_locs = res2.pn_locs;
            n_npks = res2.n_npks;
            pn_x = res2.pn_x;
            n_size = res2.n_size;
            n_min_height = res2.n_min_height;

            maxim_remove_close_peaks_ res = maxim_remove_close_peaks(pn_locs, n_npks, pn_x, n_min_distance);
            pn_locs = res.pn_locs;
            n_npks = res.pn_npks;
            pn_x = res.pn_x;
            n_min_distance = res.n_min_distance;

            n_npks = Math.Min(n_npks, n_max_num);

            rtValues.n_max_num = n_max_num;
            rtValues.n_min_distance = n_min_distance;
            rtValues.n_min_height = n_min_height;
            rtValues.n_npks = n_npks;
            rtValues.n_size = n_size;
            rtValues.pn_locs = pn_locs;
            rtValues.pn_x = pn_x;
            return rtValues;
        }

        private struct maxim_peaks_above_min_height_ { public long[] pn_locs; public int n_npks; public long[] pn_x; public int n_size; public long n_min_height; }
        private static maxim_peaks_above_min_height_ maxim_peaks_above_min_height(long[] pn_locs, int n_npks, long[] pn_x, int n_size, long n_min_height)
        {

            /*
              \brief        Find peaks above n_min_height
              \par          Details
                            Find all peaks above MIN_HEIGHT

              \retval       None
             */
            maxim_peaks_above_min_height_ rtValues = new maxim_peaks_above_min_height_();

            int i = 1, n_width;
            n_npks = 0;

            while (i < n_size - 1)
            {
                if (pn_x[i] > n_min_height && pn_x[i] > pn_x[i - 1])
                {      // find left edge of potential peaks
                    n_width = 1;
                    while (i + n_width < n_size && pn_x[i] == pn_x[i + n_width])  // find flat peaks
                        n_width++;
                    if (pn_x[i] > pn_x[i + n_width] && (n_npks) < 15)
                    {      // find right edge of peaks
                        pn_locs[(n_npks)++] = i;
                        // for flat peaks, peak location is left edge
                        i += n_width + 1;
                    }
                    else
                        i += n_width;
                }
                else
                    i++;
            }
            rtValues.n_min_height = n_min_height;
            rtValues.n_npks = n_npks;
            rtValues.n_size = n_size;
            rtValues.pn_locs = pn_locs;
            rtValues.pn_x = pn_x;
            return rtValues;
        }

        private struct maxim_remove_close_peaks_ { public long[] pn_locs; public int pn_npks; public long[] pn_x; public int n_min_distance; }
        private static maxim_remove_close_peaks_ maxim_remove_close_peaks(long[] pn_locs, int pn_npks, long[] pn_x, int n_min_distance)
        {
            /*
              \brief        Remove peaks
              \par          Details
                            Remove peaks separated by less than MIN_DISTANCE

              \retval       None
             */
            maxim_remove_close_peaks_ rtValues = new maxim_remove_close_peaks_();

            int i;
            int j;
            int n_old_npks;
            long n_dist;

            /* Order peaks from large to small */
            maxim_sort_indices_descend_ res = maxim_sort_indices_descend(pn_x, pn_locs, pn_npks);
            pn_x = res.pn_x;
            pn_locs = res.pn_indx;
            pn_npks = res.n_size;

            for (i = -1; i < pn_npks; i++)
            {
                n_old_npks = pn_npks;
                pn_npks = i + 1;
                for (j = i + 1; j < n_old_npks; j++)
                {
                    n_dist = pn_locs[j] - (i == -1 ? -1 : pn_locs[i]); // lag-zero peak of autocorr is at index -1
                    if (n_dist > n_min_distance || n_dist < -n_min_distance)
                        pn_locs[(pn_npks)++] = pn_locs[j];
                }
            }

            // Resort indices int32_to ascending order

            maxim_sort_ascend_ res2 = maxim_sort_ascend(pn_locs, pn_npks);
            pn_locs = res2.pn_x;
            pn_npks = res2.n_size;

            rtValues.n_min_distance = n_min_distance;
            rtValues.pn_x = pn_x;
            rtValues.pn_locs = pn_locs;
            rtValues.pn_npks = pn_npks;

            return rtValues;
        }

        private struct maxim_sort_ascend_ { public long[] pn_x; public int n_size; }
        private static maxim_sort_ascend_ maxim_sort_ascend(long[] pn_x, int n_size)
        {
            /*
             \brief        Sort array
             \par          Details
                           Sort array in ascending order (insertion sort algorithm)

             \retval       None
            */
            maxim_sort_ascend_ rtValues = new maxim_sort_ascend_();
            int i, j;
            long n_temp;
            for (i = 1; i < n_size; i++)
            {
                n_temp = pn_x[i];
                for (j = i; j > 0 && n_temp < pn_x[j - 1]; j--)
                    pn_x[j] = pn_x[j - 1];
                pn_x[j] = n_temp;
            }
            rtValues.n_size = n_size;
            rtValues.pn_x = pn_x;
            return rtValues;
        }

        private struct maxim_sort_indices_descend_ { public long[] pn_x; public long[] pn_indx; public int n_size; }
        private static maxim_sort_indices_descend_ maxim_sort_indices_descend(long[] pn_x, long[] pn_indx, int n_size)
        {
            /*
             \brief        Sort indices
             \par          Details
                           Sort indices according to descending order (insertion sort algorithm)

             \retval       None
            */
            maxim_sort_indices_descend_ rtValues = new maxim_sort_indices_descend_();
            int i;
            int j;
            long n_temp;
            for (i = 1; i < n_size; i++)
            {
                n_temp = pn_indx[i];
                for (j = i; j > 0 && pn_x[n_temp] > pn_x[pn_indx[j - 1]]; j--)
                    pn_indx[j] = pn_indx[j - 1];
                pn_indx[j] = n_temp;
            }
            rtValues.n_size = n_size;
            rtValues.pn_indx = pn_indx;
            rtValues.pn_x = pn_x;
            return rtValues;
        }

    }

    static class plotter
    {
        private static int scrHigh;
        private static int scrWidth;

        private static double cy;
        private static int offy;
        private static long miny;

        private static int cx = 10;

        private static Bitmap dfltArea;

        public static int neededData()
        {
            return scrWidth / cx;
        }

        public static Bitmap plot(long[] data)
        {
            int nd = data.Length;
            findYcoeff(data);

            Bitmap pltArea = dfltArea.Copy(Bitmap.Config.Argb8888, true);
            Canvas grph = new Canvas(pltArea);
            grph.SetBitmap(pltArea);

            Paint pn = new Paint(PaintFlags.AntiAlias);
            pn.SetStyle(Paint.Style.Stroke);
            pn.StrokeWidth = 3;
            pn.Color = Color.Green;

            for (int i = 0; i < nd - 1; i++)
            {
                int x1 = i * cx;
                int y1 = scrHigh - (int)((offy + data[i] - miny) * cy);

                int x2 = (i + 1) * cx;
                int y2 = scrHigh - (int)((offy + data[i + 1] - miny) * cy);

                grph.DrawLine(x1, y1, x2, y2, pn);
            }

            return pltArea;
        }

        private static void findYcoeff(long[] data)
        {
            long max, min;
            max = long.MinValue;
            min = long.MaxValue;

            foreach (int i in data)
            {
                if (i > max) max = i;
                if (i < min) min = i;
            }

            double diff = (max - min) * 1.6;
            offy = (int)((max - min) * .3);
            miny = min;
            cy = scrHigh / diff;

        }


        public static void init(int W, int H)

        {
            scrWidth = W; scrHigh = H;
            dfltArea = Bitmap.CreateBitmap(scrWidth, scrHigh, Bitmap.Config.Argb8888);

            Canvas grph = new Canvas(dfltArea);
            grph.SetBitmap(dfltArea);


            RectF fl = new RectF(0, 0, grph.Width, grph.Height);
            Paint pn = new Paint(PaintFlags.AntiAlias);
            pn.Color = Color.DarkGray;
            grph.DrawRect(fl, pn);
            pn.Color = Color.DarkGreen;
            pn.SetStyle(Paint.Style.Stroke);
            pn.StrokeWidth = 2;
            for (int x = 0; x < grph.Width; x = x + algoritm.sampleRate * cx)
            {
                grph.DrawLine(x, 0, x, grph.Height, pn);
            }

        }

    }
}

