namespace CarShow.Domain.Models
{
    public class OrderItem : BaseEntity<long>
    {
        public long OrderId { get; set; }
        public decimal CarPrice { get; set;  }
        public decimal PrePayment { get; set;  }
        public int Time { get; set;  }
        public decimal Price { get; set; }
        public bool IsConfirm { get; set; }
        public string CreateOrderItemDate { get; set; }
        #region Relations
        public Order Order { get; set; }
        #endregion
    }
}
