using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IHasProgress {


    public event EventHandler<OnProgressChnagedEventArgs> OnProgressChanged;
    public class OnProgressChnagedEventArgs : EventArgs {
        public float progressNormalized;
    }
}
