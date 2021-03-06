﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using NLog;
using Shared.DataAccess;
using Shared.Entities;

namespace Shared
{
    public class SensorDataService
    {
        private const string GetHeight = "1";

        private SensorDataRepository _sensorDataRepository;
        private SettingsRepository _settingsRepository;
        private Settings _currentSettings;

        private SensorData _lastData;
        private Logger _logger;
        private System.Timers.Timer _timer;
        private SerialPort _serialPort;

        private DateTime _currentPeriodVal;
        private DateTime _currentPeriod
        {
            get { return _currentPeriodVal; }
            set
            {
                _currentPeriodVal = value;
                _currentPeriodEnd = _currentPeriod.Add(_periodLength);
            }
        }

        private DateTime _currentPeriodEnd;
        private TimeSpan _periodLength;
        private bool _periodHasInputData;

        private IList<int> _heights;

        public SensorDataService()
        {
            _sensorDataRepository = new SensorDataRepository();
            _settingsRepository = new SettingsRepository();

            _logger = LogManager.GetCurrentClassLogger();
            _heights = new List<int>();
            _serialPort = new SerialPort();

            _timer = new System.Timers.Timer();

            _timer.Elapsed += _timer_Elapsed;
            _serialPort.DataReceived += serialPort1_DataReceived;
        }


        public void Start()
        {
            _currentSettings = _settingsRepository.LastEntrie();

            _periodLength = TimeSpan.FromMinutes(_currentSettings.IntervalLength);

            _logger.Info($"Loaded settings: {_currentSettings.GetPropertyValues()}");

            _serialPort.PortName = _currentSettings.ArduinoComPort;
            _serialPort.BaudRate = 9600;

            _timer.Interval = 5000;

            _logger.Info("Service started");
            _lastData = _sensorDataRepository.LastEntrie();

            _logger.Info(Helpers.GetPropertyValues(_lastData) ?? "No last data found");

            //In case database has newer period as handled, to prevent having same period twice
            if (_lastData != null && _lastData.DateTime.Add(TimeSpan.FromMinutes(_lastData.IntervalLength)) > DateTime.UtcNow)
            {
                _currentPeriod = _lastData.DateTime;
                _periodHasInputData = true;
                _logger.Info("Current period loaded:" + _currentPeriod.ShortDateTime());
            }
            else
            {
                _currentPeriod = GetCurrentPeriod();
                _lastData = new SensorData { DateTime = _currentPeriod, IntervalLength = _currentSettings.IntervalLength };

                _periodHasInputData = false;
                _logger.Info("Current period set:" + _currentPeriod.ShortDateTime());
            }

            _timer.Start();
        }

        private DateTime GetCurrentPeriod()
        {
            var date = DateTime.UtcNow;
            var period = (int)_periodLength.TotalMinutes;

            return new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute - (date.Minute % period), 0);
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_currentPeriodEnd < DateTime.UtcNow)
            {
                if (_periodHasInputData)
                {
                    SaveSensorData();
                }

                _logger.Info($"Old period:{_currentPeriod}, total  heights:{_heights.Count}, has input data:{_periodHasInputData}");

                //Reset for new period
                _heights.Clear();
                _periodHasInputData = false;
                _currentPeriod = GetCurrentPeriod();
                _lastData = new SensorData { DateTime = _currentPeriod, IntervalLength = _currentSettings.IntervalLength };
            }

            MonitorLastInput();

            MonitorHeight();
        }

        private void MonitorHeight()
        {
            try
            {
                if (_serialPort.IsOpen == false)
                {
                    _serialPort.Open();
                }

                _serialPort.Write(GetHeight);
            }
            catch (Exception e)
            {
                _logger.Error(e);
            }
        }

        private void MonitorLastInput()
        {
            if (_periodHasInputData == false)
            {
                var lastInput = GetLastInputDate();
                if (_currentPeriod < lastInput && lastInput < _currentPeriodEnd)
                {
                    _periodHasInputData = true;
                    _logger.Info($"Last input: {lastInput.ShortDateTime()}");
                }
            }
        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var height = Int32.Parse(_serialPort.ReadLine());

            if (height < 40 || 200 < height)
            {
                _logger.Info($"Invalid table height: {height}");
                return;
            }

            _heights.Add(height);
            _logger.Info($"Recieved height {height}, total list length:{_heights.Count}");
        }

        private DateTime GetLastInputDate()
        {
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (int)Marshal.SizeOf(lastInputInfo);

            if (!GetLastInputInfo(ref lastInputInfo))
            {
                _logger.Error("GetLastInputDate: " + GetLastError().ToString());
            }

            var lastInput = DateTime.Now.AddMilliseconds(-(Environment.TickCount - lastInputInfo.dwTime));
            _logger.Info($"Last input date:{lastInput.ShortDateTime()}, utc:{lastInput.ToUniversalTime().ShortDateTime()},  struct:{lastInputInfo.dwTime}");


            return lastInput.ToUniversalTime();
        }

        private void SaveSensorData()
        {
            _lastData.Height = (int)_heights.Average();
            _sensorDataRepository.Add(_lastData);
        }

        public void Stop()
        {
            _timer.Stop();
            _serialPort.Close();

            if (_lastData.Id == 0 && _heights.Any() && _periodHasInputData)
            {
                SaveSensorData();
            }
            _logger.Info("Service stopped");
        }

        [DllImport("User32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        internal struct LASTINPUTINFO
        {
            public Int32 cbSize;
            public Int32 dwTime;
        }

        [DllImport("Kernel32.dll")]
        private static extern uint GetLastError();

    }

}
