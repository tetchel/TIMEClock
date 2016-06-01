using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TimeClock {
    public partial class PopUp : Form {
        private int inputTB_value;
        private TimeClockContext tcc_instance = TimeClockContext.INSTANCE;

        public PopUp() {
            int current_notify_freq = tcc_instance.notify_freq_in_mins;
            inputTB_value = current_notify_freq;
            InitializeComponent();
            inputIntervalTB.Text = current_notify_freq.ToString();
        }

        private void OKButton_Click(object sender, EventArgs e) {
            //get the value of the textbox before it was changed?
            int input;
            //setNotifyFreq will display an error message
            if (!int.TryParse(inputIntervalTB.Text, out input) || input <= 0) {
                MessageBox.Show("Notification Interval must be an integer greater than 0.", "Invalid Input");
            }
            else {
                //see if user changed it
                if (inputTB_value != input) {
                    //update
                    inputTB_value = input;
                    tcc_instance.updateNotifyFreq(input);
                }

                Hide();
                inputIntervalTB.Text = input.ToString();
            }
        }
    }
}
