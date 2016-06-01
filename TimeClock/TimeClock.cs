using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.ComponentModel;

namespace TimeClock {
    static class EntryPoint {
        [STAThread]
        static void Main() {
            Application.Run(new TimeClockContext());
        }
    }

    public class TimeClockContext : ApplicationContext {
        ////////// UI objects //////////
        private NotifyIcon ni;
        private PopUp msgbox;
        //used for thread-safe UI updates
        delegate void SetTextCallback(string s);

        ////////// Constants //////////
        private const int POLL_FREQUENCY = 1;           //in seconds, how often to check if user is not locked
                                                        //decrease for increased precision and cpu usage
        private const int POLLS_TO_MINS = 60/POLL_FREQUENCY;            //# polls per minute

        private const int DEFAULT_NOTIFY_FREQUENCY = 60;

        private const int SHORT_NOTIF_DURATION  = 3000;
        private const int LONG_NOTIF_DURATION   = 6000;

        private const string TIME_FORMAT = @"hh\:mm\:ss";
        private const string APP_NAME = "TIMEClock";

        ////////// App state tracking //////////
        private DateTime start;
        //counts POLLs
        private int clock = 0;
        //toggled when user locks/unlocks workstation
        private bool workstation_locked = false;
        //terminates waiting thread when exit is called
        private bool isRunning = true;

        //notification interval values
        public  int  notify_freq_in_mins { get; }
        private int notify_freq_in_ticks;           //in ticks (this is what's used in the code).

        // Used to lock Monitor to signal sleeping thread
        private object _lock = new object();

        ////////// SINGLETON INSTANCE //////////
        public static TimeClockContext INSTANCE { get; private set; }

        public TimeClockContext() {
            INSTANCE = this;
            start = DateTime.Now;

            //initialize notify frequency fields
            //TODO get freq_in_mins from a file
            notify_freq_in_mins = DEFAULT_NOTIFY_FREQUENCY;
            notify_freq_in_ticks = notify_freq_in_mins * POLLS_TO_MINS;

            //initialize UI
            msgbox = new PopUp();

            ni = new NotifyIcon() {
                Icon = Properties.Resources.AppIcon,
                ContextMenu = new ContextMenu(new MenuItem[] { new MenuItem("Open", mainApp), new MenuItem("Exit", Exit) }),
                Visible = true
            };
            ni.DoubleClick += mainApp;
            //text is tooltip
            ni.Text = getSimpleElapsed();

            ni.BalloonTipTitle = APP_NAME + " is now running";
            ni.BalloonTipText = "Started at " + start.ToString(TIME_FORMAT) +
                "\n" + getIntervalChangedOutput();
            ni.ShowBalloonTip(LONG_NOTIF_DURATION);

            //initialize the msgbox
            msgbox.Text = APP_NAME;
            msgbox.FormBorderStyle = FormBorderStyle.FixedSingle;
            //msgbox.Size = new System.Drawing.Size(500, 200);
            //get the label object to be updated
            updateMsgBoxLabel();                       

            //locked workstation listener
            SystemEvents.SessionSwitch += new SessionSwitchEventHandler(onSessionSwitch);

            //thread that sleeps and then updates user every NOTIFY_FREQUENCY
            Thread loopthread = new Thread(new ThreadStart(loop));
            loopthread.Start();
        }

        //Thread method that loops eternally, updating the time that has passed
        //needs to be done in a separate thread so that UI thread still works
        void loop() {
            while (isRunning) {
                //wait for POLL_FREQUENCY secs or until interrupt (this is the application's "clock")
                lock(_lock) {
                    Monitor.Wait(_lock, 1000 * POLL_FREQUENCY);
                }

                //only count time and recalculate if workstation !locked
                if(!workstation_locked) {
                    clock++;
                    ni.Text = getSimpleElapsed();
                    updateMsgBoxLabel();
                }

                //display notification
                if (clock % notify_freq_in_ticks == 0) {
                    ni.BalloonTipTitle = APP_NAME;
                    ni.BalloonTipText = getNiceElapsed();
                    ni.ShowBalloonTip(SHORT_NOTIF_DURATION);
                }
            }
        }

        //allows the user to update the notify frequency
        //converts minutes to ticks and displays a message
        public void updateNotifyFreq(int new_freq_mins) {
            notify_freq_in_ticks = new_freq_mins * POLLS_TO_MINS;
            ni.BalloonTipText = getIntervalChangedOutput();
            ni.ShowBalloonTip(LONG_NOTIF_DURATION);
        }

        //called when icon is double clicked
        void mainApp(object sender, EventArgs e) {
            msgbox.ShowDialog();
        }

        ////////// String generators  //////////

        //generates string that converts ticks back to minutes 
        //and tells the user how often they will be notified
        string getIntervalChangedOutput() {
            int freq_mins = (POLL_FREQUENCY * notify_freq_in_ticks) / 60;
            return "You will be notified every "  +
                (freq_mins == 1 ? "minute" : freq_mins + " minutes") + ".";
        }

        //calculates the time elapsed and returns as string
        string getSimpleElapsed() {
            return TimeSpan.FromSeconds(clock * POLL_FREQUENCY).ToString(TIME_FORMAT);
        }

        //return the elapsed time in a nice format of hours and minutes
        //ignores seconds so no good for debugging
        string getNiceElapsed() {
            TimeSpan t = TimeSpan.FromSeconds(clock * POLL_FREQUENCY);
            string hours_str = t.Hours == 1 ? "hour" : "hours",
                    mins_str = t.Minutes == 1 ? "minute" : "minutes",
                    secs_str = t.Seconds == 1 ? "second" : "seconds";

            if (t.Seconds == 0) {
                return string.Format("{0} {1}, {2} {3}.", t.Hours, hours_str, t.Minutes, mins_str);
            }
            else {
                return string.Format("{0} {1}, {2} {3}, {4} {5}", t.Hours, hours_str, t.Minutes, mins_str, t.Seconds, secs_str);
            }
        }

        ////////// UI Updates //////////

        //call the thread-safe UI modifying function with the new message
        //called every tick
        void updateMsgBoxLabel() {
             updateMsgBoxCallback(
                 "You clocked in at " + start.ToString(TIME_FORMAT) + "\n" +
                "Working for: " + getNiceElapsed()
                );
        }

        //I don't fully understand this, but it allows thread-safe manipulation of UI elements
        void updateMsgBoxCallback(string s) {
            //callback self using Invoke
            SetTextCallback d = new SetTextCallback(updateMsgBoxCallback);
            //reacquire output_label
            Label output_label = (Label)msgbox.Controls["output"];
            if(output_label == null) {
                return;
            }
            else if (output_label.InvokeRequired) {
                output_label.Invoke(d, new object[] { s });
            }
            else {
                output_label.Text = s;
            }
        }

        ////////// OTHER //////////

        //lock workstation listener
        void onSessionSwitch(object sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionLock) {
                workstation_locked = true;
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock) {
                workstation_locked = false;
            }
        }

        //exit application
        void Exit(object sender, EventArgs e) {
            msgbox.Close();
            //interrupt the wait
            lock(_lock) {
                Monitor.Pulse(_lock);
            }
            //exit inf loop
            isRunning = false;
            //kill NI
            ni.Icon = null;
            Application.Exit();
        }
    }
}
