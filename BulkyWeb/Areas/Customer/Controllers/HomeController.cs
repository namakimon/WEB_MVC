using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models.Models;
using BulkyWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWork)
        {
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            IEnumerable<Product> productList = _unitOfWork.Product.GetAll(includeProperties: "Category");
            return View(productList);
        }

        public IActionResult Details(int? id)
        {
            Product product = _unitOfWork.Product.Get(x => x.Id == id, includeProperties: "Category");

            if (product == null)
            {
                return NotFound();
            }

            ShoppingCart shCart = new ShoppingCart()
            {
                Product = product,
                Count = 1,
                ProductId = product.Id
            };

            return View(shCart);
        }
        [HttpPost]
        [Authorize]
        public IActionResult Details(ShoppingCart cart)
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            cart.ApplicationUserId = userId;
            cart.Id = 0;

            ShoppingCart cardFromDB = _unitOfWork.ShoppingCart.Get(x => x.ApplicationUserId == userId &&
                                        x.ProductId == cart.ProductId);

            if(cardFromDB != null)
            {
                cardFromDB.Count += cart.Count;
                _unitOfWork.ShoppingCart.Update(cardFromDB);
            }
            else
            {
                _unitOfWork.ShoppingCart.Add(cart);
            }

            _unitOfWork.Save();

            TempData["success"] = "Cart updated successfully";
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}