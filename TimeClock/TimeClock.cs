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
        private PopUp msgbox = new PopUp();

        ////////// Constants //////////
        private const int POLL_FREQUENCY = 10;          //in seconds, how often to check if user is not locked
                                                        //decrease for increased precision and cpu usage
        private const int POLLS_TO_MINS = 60/POLL_FREQUENCY;            //# polls per minute
        private const int NOTIFY_FREQUENCY_MINS = 1;                    //notify user every x minutes
        //above two are just to make changing this next value easier
        private const int NOTIFY_FREQUENCY = NOTIFY_FREQUENCY_MINS*POLLS_TO_MINS;          //in ticks (this is what's used in the code).

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

        // Used to lock Monitor to signal sleeping thread
        private object _lock = new object();

        //used for thread-safe UI updates
        delegate void SetTextCallback(string s);

        public TimeClockContext() {
            start = DateTime.Now;
            //initialize UI
            ContextMenu cm = new ContextMenu(new MenuItem[] { new MenuItem("Open", mainApp), new MenuItem("Exit", Exit) });

            ni = new NotifyIcon() {
                Icon = Properties.Resources.AppIcon,
                ContextMenu = cm,
                Visible = true
            };
            ni.DoubleClick += mainApp;

            ni.BalloonTipTitle = APP_NAME + " is now running";
            //notify frequency in minutes
            int freq_mins = (POLL_FREQUENCY * NOTIFY_FREQUENCY) / 60;
            ni.BalloonTipText = "Started at " + start.ToString(TIME_FORMAT) +
                "\nYou will be notified every " + freq_mins +
                (freq_mins == 1 ? " minute" : " minutes.");
             
            ni.ShowBalloonTip(5000);
            ni.Text = getSimpleElapsed();

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

        //loop eternally, updating time every minute
        //needs to be done in a separate thread so that UI thread still works
        void loop() {
            while (isRunning) {
                //wait for POLL_FREQUENCY secs or until interrupt
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
                if (clock % NOTIFY_FREQUENCY == 0) {
                    ni.BalloonTipTitle = APP_NAME;
                    ni.BalloonTipText = getNiceElapsed();
                    ni.ShowBalloonTip(3000);
                }
            }
        }
        
        //called when icon is doubleclicked
        void mainApp(object sender, EventArgs e) {
            msgbox.Show();
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
                    mins_str = t.Minutes == 1 ? "minute" : "minutes";

            if (t.Minutes == 0) {
                return string.Format("{0} {1}.", t.Hours, hours_str);
            }
            else {
                return string.Format("{0} {1}, {2} {3}", t.Hours, hours_str, t.Minutes, mins_str);
            }
        }

        //call the thread-safe UI modifying function with the new message
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
            isRunning = false;
            ni.Visible = false;
            Application.Exit();
        }
    }
}
