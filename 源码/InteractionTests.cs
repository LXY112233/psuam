using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using QingMove;
class InteractionTests
{
    static int count;
    static List<string> failures=new List<string>();
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Case(string name,Action action){try{action();}catch(Exception ex){failures.Add(name);Console.WriteLine("FAIL "+name+": "+ex);}}
    static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
    static SettingsForm SettingsWindow()
    {
        var app=(ReminderApp)FormatterServices.GetUninitializedObject(typeof(ReminderApp));
        Action<string,object> set=delegate(string name,object value){typeof(ReminderApp).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,value);};
        set("Config",new Settings());set("Events",new List<LogEntry>());return new SettingsForm(app);
    }
    static Settings Read(SettingsForm form){return (Settings)typeof(SettingsForm).GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);}
    static ContextMenuStrip VisibleMenu()
    {
        var property=typeof(ToolStripManager).GetProperty("ToolStrips",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(var item in (IEnumerable)property.GetValue(null,null))
        {
            var target=item.GetType().GetProperty("Target",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            var menu=(target==null?item:target.GetValue(item,null)) as ContextMenuStrip;
            if(menu!=null&&menu.Visible)return menu;
        }
        throw new Exception("No visible dropdown was opened");
    }
    [STAThread]static void Main()
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new Form(){ClientSize=new Size(500,220)})
            using(var choice=new ChoiceBox(){Location=new Point(30,30),Size=new Size(350,36)})
            {
                choice.Items.AddRange(new[]{"每小时的指定分钟","每隔若干分钟","每天的指定时刻"});choice.SelectedIndex=0;form.Controls.Add(choice);form.Show();Application.DoEvents();
                Case("dropdown selection",delegate{choice.PerformClick();Application.DoEvents();var menu=VisibleMenu();menu.Items[1].PerformClick();Application.DoEvents();Check(choice.SelectedIndex==1,"dropdown selection updates value without throwing");});
                Case("dropdown repeated selection and cancellation",delegate
                {
                    for(int round=0;round<3;round++)for(int i=0;i<choice.Items.Count;i++)
                    {choice.PerformClick();Application.DoEvents();var popup=VisibleMenu();popup.Items[i].PerformClick();Application.DoEvents();Check(choice.SelectedIndex==i,"repeated selection "+round+"/"+i);}
                    choice.PerformClick();Application.DoEvents();VisibleMenu().Close(ToolStripDropDownCloseReason.Keyboard);Application.DoEvents();
                    choice.PerformClick();Application.DoEvents();VisibleMenu().Items[2].PerformClick();Application.DoEvents();Check(choice.SelectedIndex==2,"cancel and reopen works");
                });
                form.Close();
            }
            Case("stepper overflow",delegate
            {
                using(var step=new StepperBox(){Minimum=1,Maximum=120})
                {
                    var editor=(CrispTextBox)step.Controls[0];editor.Text=decimal.MaxValue.ToString();
                    foreach(Control control in step.Controls)if(control is Button&&control.Text=="+")((Button)control).PerformClick();
                    Check(step.Value==120,"huge numeric input clamps without arithmetic overflow");
                    editor.Text=decimal.MinValue.ToString();foreach(Control control in step.Controls)if(control is Button&&control.Text=="−")((Button)control).PerformClick();Check(step.Value==1,"negative extreme clamps without overflow");
                    editor.Text="119.5";foreach(Control control in step.Controls)if(control is Button&&control.Text=="+")((Button)control).PerformClick();Check(step.Value==120,"stepping fractional input yields valid integer");
                }
            });
            Case("inactive interval",delegate
            {
                using(var form=SettingsWindow())
                {
                    var mode=Field<ChoiceBox>(form,"mode");mode.SelectedIndex=1;
                    ((CrispTextBox)Field<StepperBox>(form,"interval").Controls[0]).Text="";mode.SelectedIndex=0;
                    Check(Read(form).IntervalMinutes==30,"hidden invalid interval does not block active hourly rule");
                }
            });
            Case("disabled window",delegate
            {
                using(var form=SettingsWindow())
                {
                    Field<CheckBox>(form,"window").Checked=true;Field<TimeBox>(form,"start").Text="25:99";Field<CheckBox>(form,"window").Checked=false;
                    Check(!Read(form).UseWindow&&Read(form).WindowStart=="09:00","disabled invalid time does not block saving");
                }
            });
            Case("inactive hourly text",delegate
            {
                using(var form=SettingsWindow())
                {
                    Field<FieldTextBox>(form,"minutes").Text="";Field<ChoiceBox>(form,"mode").SelectedIndex=2;
                    Check(Read(form).Mode=="daily","hidden invalid hourly input does not block daily rule");
                }
            });
            Case("time separator culture",delegate
            {
                var original=Thread.CurrentThread.CurrentCulture;
                try{Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("fi-FI");using(var time=new TimeBox()){time.Value=DateTime.Today.AddHours(9);Check(time.Value.Hour==9&&time.Text=="09:00","time inputs use stable colon format in other regional settings");}}
                finally{Thread.CurrentThread.CurrentCulture=original;}
            });
            Case("all settings dropdowns",delegate
            {
                using(var form=SettingsWindow())
                {
                    form.Show();Application.DoEvents();var nav=Field<Button[]>(form,"navigation");
                    foreach(var name in new[]{"mode","accent","sound","monitor","position"})
                    {
                        nav[name=="mode"?0:1].PerformClick();Application.DoEvents();var choice=Field<ChoiceBox>(form,name);
                        for(int round=0;round<2;round++)for(int i=0;i<choice.Items.Count;i++)
                        {choice.PerformClick();Application.DoEvents();var popup=VisibleMenu();popup.Items[i].PerformClick();Application.DoEvents();Check(choice.SelectedIndex==i,"settings dropdown "+name+" selection "+round+"/"+i);Read(form);}
                    }
                    form.AllowClose=true;form.Close();
                }
            });
            Case("active invalid input still rejected",delegate
            {
                using(var form=SettingsWindow())
                {
                    Field<CheckBox>(form,"window").Checked=true;Field<TimeBox>(form,"start").Text="25:99";
                    bool rejected=false;try{Read(form);}catch(TargetInvocationException ex){rejected=ex.InnerException is ArgumentException;}
                    Check(rejected,"invalid active time still reports validation error");
                    Field<CheckBox>(form,"window").Checked=false;Field<FieldTextBox>(form,"minutes").Text="";rejected=false;
                    try{Read(form);}catch(TargetInvocationException ex){rejected=ex.InnerException is ArgumentException;}
                    Check(rejected,"invalid active hourly value still reports validation error");
                }
            });
            if(failures.Count>0)throw new Exception(failures.Count+" failing cases: "+string.Join(", ",failures));
            Console.WriteLine("ALL "+count+" INTERACTION TESTS PASSED");
        }
        catch(Exception ex){Console.WriteLine("FAIL "+ex);Environment.ExitCode=1;}
    }
}
