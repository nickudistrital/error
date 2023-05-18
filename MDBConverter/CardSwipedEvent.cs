using System;
namespace POS
{
    public delegate void CardSwipedEventHandler(object sender, CardSwipedEventArgs args);

    public class CardSwipedEventArgs : EventArgs
    {
        public string Bin { get; set; }
        public bool IsCardSwiped { get; set; }
    }
}
