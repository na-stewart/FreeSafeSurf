﻿namespace Enforcer
{
    partial class Main
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            SuspendLayout();
            // 
            // Main
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(283, 175);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Main";
            Text = "SafeSurf";
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            ResumeLayout(false);
        }

        #endregion
    }
}
