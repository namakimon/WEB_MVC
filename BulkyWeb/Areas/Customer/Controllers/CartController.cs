using Bulky.DataAccess.Repository;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; }
        public CartController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            IEnumerable<ShoppingCart> shoppingCartList = _unitOfWork.ShoppingCart
                .GetAll(x => x.ApplicationUserId == userId, "Product")
                .Select(x => { x.Price = GetProductTotalPrice(x); return x; });

            var orderTotalPrice = shoppingCartList.Sum(x => x.Count * x.Price);

            ShoppingCartVM = new ShoppingCartVM
            {
                ShoppingCartList = shoppingCartList,
                OrderHeader = new OrderHeader { OrderTotal = orderTotalPrice }
            };
            return View(ShoppingCartVM);
        }

        public IActionResult Summary()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            IEnumerable<ShoppingCart> shoppingCartList = _unitOfWork.ShoppingCart
                .GetAll(x => x.ApplicationUserId == userId, "Product")
                .Select(x => { x.Price = GetProductTotalPrice(x); return x; });

            var orderTotalPrice = shoppingCartList.Sum(x => x.Count * x.Price);
            ApplicationUser user = _unitOfWork.ApplicationUser.Get(x => x.Id == userId);

            ShoppingCartVM = new ShoppingCartVM
            {
                ShoppingCartList = shoppingCartList,
                OrderHeader = new OrderHeader
                {
                    OrderTotal = orderTotalPrice,
                    ApplicationUser = user,
                    Name = user.Name,
                    PhoneNumber = user.PhoneNumber,
                    StreetAddress = user.StreetAddress,
                    City = user.City,
                    State = user.State,
                    PostalCode = user.PostalCode
                }
            };
            return View(ShoppingCartVM);
        }
        [HttpPost]
        [ActionName(nameof(Summary))]
        public IActionResult SummaryPost()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            ShoppingCartVM.ShoppingCartList = _unitOfWork.ShoppingCart
                .GetAll(x => x.ApplicationUserId == userId, includeProperties: "Product")
                .Select(x => { x.Price = GetProductTotalPrice(x); return x; });

            var orderTotalPrice = ShoppingCartVM.ShoppingCartList.Sum(x => x.Count * x.Price);
            var user = _unitOfWork.ApplicationUser.Get(x => x.Id == userId);
            bool isCustomerAcc = (user?.CompanyId.GetValueOrDefault() ?? 0) == 0;

            ShoppingCartVM.OrderHeader.OrderDate = DateTime.Now;
            ShoppingCartVM.OrderHeader.ApplicationUserId = userId;
            ShoppingCartVM.OrderHeader.OrderTotal = orderTotalPrice;

            if (isCustomerAcc)
            {//It's a customer acc
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
            }
            else
            {//company user
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
            }

            _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
            _unitOfWork.Save();

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                OrderDetail orderDetail = new()
                {
                    ProductId = cart.ProductId,
                    OrderHeaderId = ShoppingCartVM.OrderHeader.Id,
                    Price = cart.Price,
                    Count = cart.Count
                };
                _unitOfWork.OrderDetail.Add(orderDetail);
            }
            _unitOfWork.Save();

            if (isCustomerAcc)
            {
                string domain = "https://localhost:7205/";
                var options = new SessionCreateOptions
                {
                    SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
                    CancelUrl = domain + "customer/cart/index",
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment",
                };

                foreach(var item in ShoppingCartVM.ShoppingCartList)
                {
                    SessionLineItemOptions sessionLineItem = new ()
                    {
                        PriceData = new SessionLineItemPriceDataOptions()
                        {
                            UnitAmount = (long)(item.Price * 100),
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions()
                            {
                                Name = item.Product.Title
                            }
                        },
                        Quantity = item.Count
                    };
                    options.LineItems.Add(sessionLineItem);
                }

                SessionService service = new ();
                Session session = service.Create(options);

                _unitOfWork.OrderHeader.UpdateStripePaymentID(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();

                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);
            }

            return RedirectToAction(nameof(OrderConfirmation), new { id = ShoppingCartVM.OrderHeader.Id });
        }

        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(x=>x.Id == id, includeProperties: "ApplicationUser");

            if (orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment)
            {//order by customer
                SessionService service = new ();
                Session session = service.Get(orderHeader.SessionId);

                if(session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStripePaymentID(id, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }

            _unitOfWork.ShoppingCart.RemoveRange(_unitOfWork.ShoppingCart.GetAll(x => x.ApplicationUserId == orderHeader.ApplicationUserId));
            _unitOfWork.Save();

            return View(id);
        }

        public IActionResult Plus(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(x => x.Id == cartId);

            if (cartFromDB == null)
            {
                return NotFound();
            }

            cartFromDB.Count++;
            _unitOfWork.ShoppingCart.Update(cartFromDB);
            _unitOfWork.Save();

            return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(x => x.Id == cartId);

            if (cartFromDB == null)
            {
                return NotFound();
            }

            if (cartFromDB.Count > 1)
            {
                cartFromDB.Count--;
                _unitOfWork.ShoppingCart.Update(cartFromDB);
            }
            else
            {
                _unitOfWork.ShoppingCart.Remove(cartFromDB);
            }

            _unitOfWork.Save();
            HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart.GetAll(x => x.ApplicationUserId == cartFromDB.ApplicationUserId).Count());
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Remove(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(x => x.Id == cartId);

            if (cartFromDB == null)
            {
                return NotFound();
            }

            _unitOfWork.ShoppingCart.Remove(cartFromDB);
            _unitOfWork.Save();

            HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart.GetAll(x => x.ApplicationUserId == cartFromDB.ApplicationUserId).Count());
            return RedirectToAction(nameof(Index));
        }

        private double GetProductTotalPrice(ShoppingCart cart)
        {
            double localPrice;

            if (cart.Count > 100)
            {
                localPrice = cart.Product.Price100;
            }
            else if (cart.Count > 50)
            {
                localPrice = cart.Product.Price50;
            }
            else
            {
                localPrice = cart.Product.Price;
            }

            return localPrice;
        }
    }
}
