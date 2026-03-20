namespace SMBVB_GNSS
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            grNetwork = new DevExpress.XtraEditors.GroupControl();
            lblIp = new DevExpress.XtraEditors.LabelControl();
            textEdit1 = new DevExpress.XtraEditors.TextEdit();
            lblSCPIPort = new DevExpress.XtraEditors.LabelControl();
            textEdit2 = new DevExpress.XtraEditors.TextEdit();
            lblUDPPort = new DevExpress.XtraEditors.LabelControl();
            textEdit3 = new DevExpress.XtraEditors.TextEdit();
            btnConnect = new DevExpress.XtraEditors.SimpleButton();
            btnDisconnect = new DevExpress.XtraEditors.SimpleButton();
            pictureEdit1 = new DevExpress.XtraEditors.PictureEdit();
            ((System.ComponentModel.ISupportInitialize)grNetwork).BeginInit();
            grNetwork.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)textEdit1.Properties).BeginInit();
            ((System.ComponentModel.ISupportInitialize)textEdit2.Properties).BeginInit();
            ((System.ComponentModel.ISupportInitialize)textEdit3.Properties).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureEdit1.Properties).BeginInit();
            SuspendLayout();
            // 
            // grNetwork
            // 
            grNetwork.Controls.Add(pictureEdit1);
            grNetwork.Controls.Add(btnDisconnect);
            grNetwork.Controls.Add(btnConnect);
            grNetwork.Controls.Add(textEdit3);
            grNetwork.Controls.Add(lblUDPPort);
            grNetwork.Controls.Add(textEdit2);
            grNetwork.Controls.Add(lblSCPIPort);
            grNetwork.Controls.Add(textEdit1);
            grNetwork.Controls.Add(lblIp);
            grNetwork.Location = new System.Drawing.Point(12, 12);
            grNetwork.Name = "grNetwork";
            grNetwork.Size = new System.Drawing.Size(580, 85);
            grNetwork.TabIndex = 0;
            grNetwork.Text = "네트워크 연결";
            // 
            // lblIp
            // 
            lblIp.Location = new System.Drawing.Point(5, 50);
            lblIp.Name = "lblIp";
            lblIp.Size = new System.Drawing.Size(35, 14);
            lblIp.TabIndex = 0;
            lblIp.Text = "장비 IP";
            // 
            // textEdit1
            // 
            textEdit1.Location = new System.Drawing.Point(46, 47);
            textEdit1.Name = "textEdit1";
            textEdit1.Size = new System.Drawing.Size(100, 20);
            textEdit1.TabIndex = 1;
            // 
            // lblSCPIPort
            // 
            lblSCPIPort.Location = new System.Drawing.Point(161, 50);
            lblSCPIPort.Name = "lblSCPIPort";
            lblSCPIPort.Size = new System.Drawing.Size(52, 14);
            lblSCPIPort.TabIndex = 2;
            lblSCPIPort.Text = "SCPI Port";
            // 
            // textEdit2
            // 
            textEdit2.Location = new System.Drawing.Point(219, 47);
            textEdit2.Name = "textEdit2";
            textEdit2.Size = new System.Drawing.Size(44, 20);
            textEdit2.TabIndex = 3;
            // 
            // lblUDPPort
            // 
            lblUDPPort.Location = new System.Drawing.Point(269, 50);
            lblUDPPort.Name = "lblUDPPort";
            lblUDPPort.Size = new System.Drawing.Size(50, 14);
            lblUDPPort.TabIndex = 4;
            lblUDPPort.Text = "UDP Port";
            // 
            // textEdit3
            // 
            textEdit3.Location = new System.Drawing.Point(325, 47);
            textEdit3.Name = "textEdit3";
            textEdit3.Size = new System.Drawing.Size(49, 20);
            textEdit3.TabIndex = 5;
            // 
            // btnConnect
            // 
            btnConnect.Location = new System.Drawing.Point(407, 46);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new System.Drawing.Size(43, 23);
            btnConnect.TabIndex = 6;
            btnConnect.Text = "연결";
            // 
            // btnDisconnect
            // 
            btnDisconnect.Location = new System.Drawing.Point(456, 46);
            btnDisconnect.Name = "btnDisconnect";
            btnDisconnect.Size = new System.Drawing.Size(43, 23);
            btnDisconnect.TabIndex = 7;
            btnDisconnect.Text = "해제";
            // 
            // pictureEdit1
            // 
            pictureEdit1.Location = new System.Drawing.Point(517, 40);
            pictureEdit1.Name = "pictureEdit1";
            pictureEdit1.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Auto;
            pictureEdit1.Size = new System.Drawing.Size(43, 29);
            pictureEdit1.TabIndex = 8;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(602, 513);
            Controls.Add(grNetwork);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "Form1";
            Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)grNetwork).EndInit();
            grNetwork.ResumeLayout(false);
            grNetwork.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)textEdit1.Properties).EndInit();
            ((System.ComponentModel.ISupportInitialize)textEdit2.Properties).EndInit();
            ((System.ComponentModel.ISupportInitialize)textEdit3.Properties).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureEdit1.Properties).EndInit();
            ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.GroupControl grNetwork;
        private DevExpress.XtraEditors.TextEdit textEdit3;
        private DevExpress.XtraEditors.LabelControl lblUDPPort;
        private DevExpress.XtraEditors.TextEdit textEdit2;
        private DevExpress.XtraEditors.LabelControl lblSCPIPort;
        private DevExpress.XtraEditors.TextEdit textEdit1;
        private DevExpress.XtraEditors.LabelControl lblIp;
        private DevExpress.XtraEditors.SimpleButton btnDisconnect;
        private DevExpress.XtraEditors.SimpleButton btnConnect;
        private DevExpress.XtraEditors.PictureEdit pictureEdit1;
    }
}

