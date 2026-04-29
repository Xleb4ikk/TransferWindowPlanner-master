namespace StandaloneTrajectoryCalculator.Gui;

partial class Form1
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(243, 239, 232);
        ClientSize = new Size(1560, 980);
        MinimumSize = new Size(1040, 700);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Standalone Trajectory Calculator";
    }
}
