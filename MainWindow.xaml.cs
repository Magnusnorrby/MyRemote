//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.BodyBasics
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Speech.Synthesis;
    using System.Text;
    using System.Windows;
    using System.Windows.Documents;
    using System.Windows.Forms;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using Microsoft.Speech.AudioFormat;
    using Microsoft.Speech.Recognition;



     /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Allows the system to simulate mouse events
        /// </summary>
        [DllImport("user32.dll")]
        static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        /// <summary>
        /// Allows the system to simulate keyboard events
        /// </summary>
        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, int dwFlaags, int dwExtraInfo);

        /// <summary>
        /// Mouse variables
        /// </summary>
        private const int MOUSEEVENT_LEFTDOWN = 0x02;
        private const int MOUSEEVENT_LEFTUP = 0x04;
        private const int MOUSEEVENT_RIGHTDOWN = 0x08;
        private const int MOUSEEVENT_RIGHTUP = 0x10;
        private const int MOUSEEVENT_MOUSEWHEEL = 0x0800;

        /// <summary>
        /// Keyboard variables
        /// </summary>

        private const int KEYEVENT_DOWN = 0x0001; // key up
        private const int KEYEVENT_UP = 0x0002;  // key down
        private const int VK_CTR = 0xA2; // CTR-key

       
        /// <summary>
        /// TackingId for the person driving the app
        /// </summary>
        private long driver = 0;

        /// <summary>
        /// For voice activation
        /// </summary>
        private bool activate = false;

        /// <summary>
        /// Old hand position used for calculating cursor movement
        /// </summary>
        private System.Drawing.Point oldPoint;

        /// <summary>
        /// Radius of drawn hand circles
        /// </summary>
        private const double HandSize = 30;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Constant for clamping Z values of camera space points from being negative
        /// </summary>
        private const float InferredZPositionClamp = 0.1f;

        /// <summary>
        /// Brush used for drawing hands that are currently tracked as closed
        /// </summary>
        private readonly Brush handClosedBrush = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));

        /// <summary>
        /// Brush used for drawing hands that are currently tracked as opened
        /// </summary>
        private readonly Brush handOpenBrush = new SolidColorBrush(Color.FromArgb(128, 0, 255, 0));

        /// <summary>
        /// Brush used for drawing hands that are currently tracked as in lasso (pointer) position
        /// </summary>
        private readonly Brush handLassoBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 255));

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;


        /// <summary>
        /// Drawing group for body rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Stream for 32b-16b conversion.
        /// </summary>
        private KinectAudioStream convertStream = null;

        /// <summary>
        /// Speech recognition engine using audio data from Kinect.
        /// </summary>
        private SpeechRecognitionEngine speechEngine = null;

        /// <summary>
        /// Creates voice messages
        /// </summary>
        private SpeechSynthesizer theVoice = new SpeechSynthesizer();

        /// <summary>
        /// Coordinate mapper to map one type of point to another
        /// </summary>
        private CoordinateMapper coordinateMapper = null;

        /// <summary>
        /// Reader for body frames
        /// </summary>
        private BodyFrameReader bodyFrameReader = null;

        /// <summary>
        /// Array for the bodies
        /// </summary>
        private Body[] bodies = null;

        /// <summary>
        /// definition of bones
        /// </summary>
        private List<Tuple<JointType, JointType>> bones;

        /// <summary>
        /// Width of display (depth space)
        /// </summary>
        private int displayWidth;

        /// <summary>
        /// Height of display (depth space)
        /// </summary>
        private int displayHeight;

        /// <summary>
        /// List of colors for each body tracked
        /// </summary>
        private List<Pen> bodyColors;

        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;

        /// <summary>
        /// Current status text to display
        /// </summary>
        private HandState oldLeftHandState;

        /// <summary>
        /// Lock for speech events
        /// </summary>
        private readonly object speechLock = new object();



        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            // one sensor is currently supported
            this.kinectSensor = KinectSensor.GetDefault();

            // get the coordinate mapper
            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            // get the depth (display) extents
            FrameDescription frameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;

            // get size of joint space
            this.displayWidth = frameDescription.Width;
            this.displayHeight = frameDescription.Height;

            // open the reader for the body frames
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();

 
            // populate body colors, one for each BodyIndex
            this.bodyColors = new List<Pen>();

            this.bodyColors.Add(new Pen(Brushes.Red, 6));
            this.bodyColors.Add(new Pen(Brushes.Orange, 6));
            this.bodyColors.Add(new Pen(Brushes.Green, 6));
            this.bodyColors.Add(new Pen(Brushes.Blue, 6));
            this.bodyColors.Add(new Pen(Brushes.Indigo, 6));
            this.bodyColors.Add(new Pen(Brushes.Violet, 6));



            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // grab the audio stream
            IReadOnlyList<AudioBeam> audioBeamList = this.kinectSensor.AudioSource.AudioBeams;
            System.IO.Stream audioStream = audioBeamList[0].OpenInputStream();

            // create the convert stream
            this.convertStream = new KinectAudioStream(audioStream);

            

            // set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // initialize the components (controls) of the window
            this.InitializeComponent();
        }

        /// <summary>
        /// Gets the metadata for the speech recognizer (acoustic model) most suitable to
        /// process audio from Kinect device.
        /// </summary>
        /// <returns>
        /// RecognizerInfo if found, <code>null</code> otherwise.
        /// </returns>
        private static RecognizerInfo TryGetKinectRecognizer()
        {
            IEnumerable<RecognizerInfo> recognizers;

            // This is required to catch the case when an expected recognizer is not installed.
            // By default - the x86 Speech Runtime is always expected. 
            try
            {
                recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            }
            catch (COMException)
            {
                return null;
            }

            foreach (RecognizerInfo recognizer in recognizers)
            {
                string value;
                recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
                if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase)) //Language package used
                {
                    return recognizer;
                }
            }

            return null;
        }

        /// <summary>
        /// Handler for recognized speech events.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            lock (speechLock)
            {
                // Speech utterance confidence below which we treat speech as if it hadn't been heard
                const double ConfidenceThreshold = 0.3;
                if (e.Result.Confidence >= ConfidenceThreshold & e.Result.Text != null)
                {
                    switch (e.Result.Text)
                    {
                        case "activate":
                            if (!activate)
                            {
                                activate = true;
                                theVoice.Speak("Activating remote controll");
                            }
                            else
                            {
                                theVoice.Speak("Already activated");
                            }

                            break;

                        case "open graphics": // GLMol
                            theVoice.Speak("Launching G L Mol");
                            System.Diagnostics.Process.Start("chrome", "C:/Users/kptg125/Desktop/GLMol/viewer.html");
                            break;

                        case "python mole": //pyMol
                            theVoice.Speak("Launching Pymol");
                            System.Diagnostics.Process.Start("C:/Users/kptg125/Desktop/pymol/PyMOL/PymolWin.exe", "C:/Users/kptg125/Desktop/pymol/PyMOL/2DHB.pdb");
                            break;

                        case "calculated lab": //clab
                            theVoice.Speak("Launching C Lab");
                            System.Diagnostics.Process.Start("chrome", "http://clab.rd.astrazeneca.net:8000/dashboard#/");
                            break;

                        case "break":
                            if (activate)
                            {
                                activate = false;
                                theVoice.Speak("Deactivating remote controll");
                            }
                            else
                            {
                                theVoice.Speak("Already deactivated");
                            }

                            break;

                        case "shut down":
                            theVoice.Speak("Shutting down, Good bye");
                            this.Close();

                            break;

                        case "are you alive":
                            theVoice.Speak("Hello, I am still listening");
                            break;

                        case "help me":
                            theVoice.Speak("Commands written to console");
                            printHelp();

                            break;



                    }
                }
            }

        }

        /// <summary>
        /// Handler for rejected speech events.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void SpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            
        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.imageSource;
            }
        }

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        /// <summary>
        /// Prints the possible voice commands
        /// </summary>
        public void printHelp()
        {
            Console.Out.Write("\n 'Activate' - starts remote control \n 'Stop' - stops remote control \n 'shut down' - exits the program \n 'open graphics' - launches GLMol \n 'PyMol' - launches PyMol \n 'Calculated lab' - launches cLab \n");
        }

        /// <summary>
        /// Execute start up tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RecognizerInfo ri = TryGetKinectRecognizer();
            if (null != ri)
            {

                this.speechEngine = new SpeechRecognitionEngine(ri.Id);


                var commands = new Choices();
                commands.Add("activate");
                commands.Add("break");
                commands.Add("open graphics");  // GLMol
                commands.Add("calculated lab"); //clab
                commands.Add("python mole");  //pymol
                commands.Add("shut down");
                commands.Add("are you alive");
                commands.Add("help me");
          
 

                var gb = new GrammarBuilder { Culture = ri.Culture };
                gb.Append(commands);

                var g = new Grammar(gb);
                this.speechEngine.LoadGrammar(g);

                lock (speechLock)
                {
                    this.speechEngine.SpeechRecognized += this.SpeechRecognized;
                    this.speechEngine.SpeechRecognitionRejected += this.SpeechRejected;
                }
              

                // let the convertStream know speech is going active
                this.convertStream.SpeechActive = true;

                // For long recognition sessions (a few hours or more), it may be beneficial to turn off adaptation of the acoustic model. 
                // This will prevent recognition accuracy from degrading over time.
                ////speechEngine.UpdateRecognizerSetting("AdaptationOn", 0);

                this.speechEngine.SetInputToAudioStream(
                    this.convertStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                this.speechEngine.RecognizeAsync(RecognizeMode.Multiple);
            }

            theVoice.SelectVoice("Microsoft Zira Desktop"); // Sets the speaking voice

            theVoice.Speak("Ready");
            printHelp();

            if (this.bodyFrameReader != null)
            {
                this.bodyFrameReader.FrameArrived += this.Reader_FrameArrived;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            int X = System.Windows.Forms.Cursor.Position.X;
            int Y = System.Windows.Forms.Cursor.Position.Y;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                mouse_event(MOUSEEVENT_LEFTUP, X, Y, 0, 0);
            }
            else if (Mouse.RightButton == MouseButtonState.Pressed)
            {
                mouse_event(MOUSEEVENT_RIGHTUP, X, Y, 0, 0);
            }

            if (this.bodyFrameReader != null)
            {
                // BodyFrameReader is IDisposable
                this.bodyFrameReader.Dispose();
                this.bodyFrameReader = null;
            }

            if (null != this.convertStream)
            {
                this.convertStream.SpeechActive = false;
            }

            if (null != this.speechEngine)
            {
                this.speechEngine.SpeechRecognized -= this.SpeechRecognized;
                this.speechEngine.SpeechRecognitionRejected -= this.SpeechRejected;
                this.speechEngine.RecognizeAsyncStop();
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

 
        /// <summary>
        /// Handles the body frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {


            bool dataReceived = false;

            using (BodyFrame bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    if (this.bodies == null)
                    {
                        this.bodies = new Body[bodyFrame.BodyCount];
                    }

                    // The first time GetAndRefreshBodyData is called, Kinect will allocate each Body in the array.
                    // As long as those body objects are not disposed and not set to null in the array,
                    // those body objects will be re-used.
                    bodyFrame.GetAndRefreshBodyData(this.bodies);
                    dataReceived = true;
                }
            }

            if (dataReceived)
            {
                using (DrawingContext dc = this.drawingGroup.Open())
                {
                    // Draw a transparent background to set the render size
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, this.displayWidth, this.displayHeight));

                    int penIndex = 0;

                    bool driverExists = false;

                    if (!activate)  // if the app isn't activated
                    {
                        return;
                    }

                    foreach (Body body in this.bodies)
                    {
                        Pen drawPen = this.bodyColors[penIndex++];

                        if (body.IsTracked)
                        {
                            this.DrawClippedEdges(body, dc);
                            IReadOnlyDictionary<JointType, Joint> joints = body.Joints;

                            // convert the joint points to depth (display) space
                            Dictionary<JointType, Point> jointPoints = new Dictionary<JointType, Point>();



                            foreach (JointType jointType in joints.Keys)
                            {
                                // sometimes the depth(Z) of an inferred joint may show as negative
                                // clamp down to 0.1f to prevent coordinatemapper from returning (-Infinity, -Infinity)
                                CameraSpacePoint position = joints[jointType].Position;
                                if (position.Z < 0)
                                {
                                    position.Z = InferredZPositionClamp;
                                }

                                DepthSpacePoint depthSpacePoint = this.coordinateMapper.MapCameraPointToDepthSpace(position);
                                jointPoints[jointType] = new Point(depthSpacePoint.X, depthSpacePoint.Y);
                            }

                            this.DrawBody(joints, jointPoints, dc, drawPen);
                            if (driver == 0) //if no driver has been assigned we assign one
                            {
                                driver = (long)body.TrackingId;
                            }
                            if (driver==(long)body.TrackingId) //Makes sure we only track one persons hands 
                            {

                                driverExists = true; // the driver is still in screen
                                
                                // if the hand is positioned above the wrist we analyse it's position and state
                                if ((int)jointPoints[JointType.HandRight].Y < (int)jointPoints[JointType.WristRight].Y) 
                                {
                                    this.AnalyseRightHand(body.HandRightState, jointPoints[JointType.HandRight], dc, oldPoint);
                                }
                                oldPoint.X = (int)jointPoints[JointType.HandRight].X;
                                oldPoint.Y = (int)jointPoints[JointType.HandRight].Y;
                                if ((int)jointPoints[JointType.HandLeft].Y < (int)jointPoints[JointType.WristLeft].Y)
                                {
                                    this.AnalyseLeftHand(body.HandLeftState, jointPoints[JointType.HandLeft], dc);
                                }

                            }
                        }

                        if (!driverExists)  // no driver exists, reset the driver id so we can find a new driver
                        {
                            driver = 0;
                        }

                       
                    }

                    // prevent drawing outside of our render area
                    this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, this.displayWidth, this.displayHeight));
                }
            }
        }


        /// <summary>
        /// Draws a body
        /// </summary>
        /// <param name="joints">joints to draw</param>
        /// <param name="jointPoints">translated positions of joints to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="drawingPen">specifies color to draw a specific body</param>
        private void DrawBody(IReadOnlyDictionary<JointType, Joint> joints, IDictionary<JointType, Point> jointPoints, DrawingContext drawingContext, Pen drawingPen)
        {


            // Draw the joints
            foreach (JointType jointType in joints.Keys)
            {
                Brush drawBrush = null;

                TrackingState trackingState = joints[jointType].TrackingState;

                if (trackingState == TrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (trackingState == TrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, jointPoints[jointType], JointThickness, JointThickness);
                }
            }
        }


        /// <summary>
        /// Draws a hand symbol if the hand is tracked: red circle = closed, green circle = opened; blue circle = lasso
        /// </summary>
        /// <param name="handState">state of the hand</param>
        /// <param name="handPosition">position of the hand</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void AnalyseRightHand(HandState handState, Point handPosition, DrawingContext drawingContext, System.Drawing.Point oldPoint)
        {
            int X = (int)handPosition.X;
            int Y = (int)handPosition.Y;
                switch (handState)
                {
                    case HandState.Closed:
                        drawingContext.DrawEllipse(this.handClosedBrush, null, handPosition, HandSize, HandSize);
                        if (oldPoint != null)
                        {
                            System.Drawing.Point point = System.Windows.Forms.Cursor.Position;
                            point.X += (X - oldPoint.X) * 7;
                            point.Y += (Y - oldPoint.Y) * 7;
                            System.Windows.Forms.Cursor.Position = point;
                        }
                        break;

                    case HandState.Open:
                        drawingContext.DrawEllipse(this.handOpenBrush, null, handPosition, HandSize, HandSize);
                        break;

                    case HandState.Lasso:
                        drawingContext.DrawEllipse(this.handLassoBrush, null, handPosition, HandSize, HandSize);
                        if (oldPoint != null)
                        {

                            int delta = (X - oldPoint.X) * 30;
                            mouse_event(MOUSEEVENT_MOUSEWHEEL, System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y, delta, 0);
                        }

                        break;
                }
        }

        private void AnalyseLeftHand(HandState handState, Point handPosition, DrawingContext drawingContext)
        {
            int X = System.Windows.Forms.Cursor.Position.X;
            int Y = System.Windows.Forms.Cursor.Position.Y;
                switch (handState)
                {
                    case HandState.Closed:
                        drawingContext.DrawEllipse(this.handClosedBrush, null, handPosition, HandSize, HandSize);
                        //if (Mouse.RightButton == MouseButtonState.Pressed)
                        if(oldLeftHandState!=HandState.Closed)
                        {
                            mouse_event(MOUSEEVENT_RIGHTUP, X, Y, 0, 0);
                            mouse_event(MOUSEEVENT_LEFTDOWN, X, Y, 0, 0);
                        }

                        break;

                    case HandState.Open:
                        drawingContext.DrawEllipse(this.handOpenBrush, null, handPosition, HandSize, HandSize);
                        mouse_event(MOUSEEVENT_LEFTUP, X, Y, 0, 0);
                        mouse_event(MOUSEEVENT_RIGHTUP, X, Y, 0, 0);

                        break;

                    case HandState.Lasso:
                        drawingContext.DrawEllipse(this.handLassoBrush, null, handPosition, HandSize, HandSize);
                        if (Mouse.RightButton == MouseButtonState.Released)
                        {
                            mouse_event(MOUSEEVENT_RIGHTDOWN, X, Y, 0, 0);
                        }

                        if (Mouse.LeftButton == MouseButtonState.Pressed)
                        {
                            mouse_event(MOUSEEVENT_LEFTUP, X, Y, 0, 0);
                        }


                        break;
                }
                oldLeftHandState = handState;
           
        }

        /// <summary>
        /// Draws indicators to show which edges are clipping body data
        /// </summary>
        /// <param name="body">body to draw clipping information for</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawClippedEdges(Body body, DrawingContext drawingContext)
        {
            FrameEdges clippedEdges = body.ClippedEdges;

            if (clippedEdges.HasFlag(FrameEdges.Bottom))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, this.displayHeight - ClipBoundsThickness, this.displayWidth, ClipBoundsThickness));
            }

            if (clippedEdges.HasFlag(FrameEdges.Top))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, this.displayWidth, ClipBoundsThickness));
            }

            if (clippedEdges.HasFlag(FrameEdges.Left))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, ClipBoundsThickness, this.displayHeight));
            }

            if (clippedEdges.HasFlag(FrameEdges.Right))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(this.displayWidth - ClipBoundsThickness, 0, ClipBoundsThickness, this.displayHeight));
            }
        }

        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }
    }
}
