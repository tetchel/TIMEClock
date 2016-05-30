using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TimeClock {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            Application.Run(new TimeClockContext());
        }
    }

    public class TimeClockContext : ApplicationContext {
        private NotifyIcon ni;
        private DateTime start;
        private MenuItem timer;

        private const int POLL_FREQUENCY = 10;          //in seconds, how often to check if user is not locked
                                                        //decrease for increased (precision and cpu usage)
        private const int POLLS_TO_MINS = 60/POLL_FREQUENCY;            //# polls per minute
        private const int NOTIFY_FREQUENCY_MINS = 60;                   //notify user every x minutes
        //above two are just to make changing this next value easier
        private const int NOTIFY_FREQUENCY = NOTIFY_FREQUENCY_MINS*POLLS_TO_MINS;          //in ticks (this is what's used in the code).

        private const string time_format = @"hh\:mm\:ss";

        //counts POLLs
        private int clock = 0;
        //toggled when user locks/unlocks workstation
        private bool workstation_locked = false;
        //terminates waiting thread when exit is called
        private bool isRunning = true;

        private object _lock = new object();

        public TimeClockContext() {
            start = DateTime.Now;
            timer = new MenuItem();
            ContextMenu cm = new ContextMenu(new MenuItem[] { timer, new MenuItem("Open", mainApp), new MenuItem("Exit", Exit) });
            cm.Popup += onPopUp;

            ni = new NotifyIcon() {
                Icon = Properties.Resources.AppIcon,
                ContextMenu = cm,
                Visible = true
            };
            ni.DoubleClick += mainApp;
            ni.BalloonTipTitle = "TIMEClock is now running";
            ni.BalloonTipText = "Started at " + start.ToString(time_format) + "\nYou will be notified every " + POLL_FREQUENCY * NOTIFY_FREQUENCY + "s";
            ni.ShowBalloonTip(5000);

            //locked workstation listener
            SystemEvents.SessionSwitch += new SessionSwitchEventHandler(SystemEvents_SessionSwitch);

            //thread that sleeps and then updates user every NOTIFY_FREQUENCY
            Thread loopthread = new Thread(new ThreadStart(loop));
            loopthread.Start();
        }

        //loop eternally, updating time every minute
        //needs to be done in a separate thread so that UI thread still works
        void loop() {
            while (isRunning) {
                lock(_lock) {
                    Monitor.Wait(_lock, 1000 * POLL_FREQUENCY);
                }
                //only count time if workstation !locked
                if(!workstation_locked) {
                    clock++;
                }

                //notify when locked
                if (clock % NOTIFY_FREQUENCY == 0) {
                    ni.BalloonTipTitle = "TIMEClock";
                    ni.BalloonTipText = "You have been working for " + getElapsed();
                    ni.ShowBalloonTip(3000);
                }
            }
        }

        //called when context menu is opened
        void onPopUp(object sender, EventArgs e) {
            timer.Text = getElapsed();
        }

        void mainApp(object sender, EventArgs e) {
            //TODO update while dialog is open (even better, when context menu is open too)
            MessageBox.Show("You clocked in at " + start.ToString(time_format) + "\n" +
                            "Working for: " + getElapsed());
        }

        //calculates the time elapsed and returns as string
        string getElapsed() {
           return DateTime.Now.Subtract(start).ToString(time_format);
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionLock) {
                workstation_locked = true;
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock) {
                workstation_locked = false;
            }
        }

        //exit application
        void Exit(object sender, EventArgs e) {
            lock(_lock) {
                Monitor.Pulse(_lock);
            }
            isRunning = false;
            ni.Visible = false;
            Application.Exit();
        }
    }
}
