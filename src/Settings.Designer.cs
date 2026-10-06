namespace SubScreen
{
    partial class Settings
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
            this.components = new System.ComponentModel.Container();
            // 具体的设置项在 Settings.BuildUi() 里用代码构建：
            // 这个工程有五个分页、几十个控件，手工维护一份 Designer 代码
            // 既容易出错也不便阅读，所以布局逻辑集中在 Settings.cs。
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(760, 660);
            this.Name = "Settings";
            this.Text = "SubScreen 设置";
        }

        #endregion
    }
}
