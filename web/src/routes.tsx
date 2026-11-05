import { createBrowserRouter } from 'react-router'
import { AccountPage } from './features/account/AccountPage'
import { AddressesPage } from './features/addresses/AddressesPage'
import { ForgotPasswordPage, ResetPasswordPage } from './features/account/PasswordResetPages'
import { RegisterPage } from './features/account/RegisterPage'
import { SignInPage } from './features/account/SignInPage'
import { CartPage } from './features/cart/CartPage'
import { CheckoutPage } from './features/checkout/CheckoutPage'
import { OrderConfirmationPage } from './features/checkout/OrderConfirmationPage'
import { ProductDetailPage } from './features/catalog/ProductDetailPage'
import { ProductListPage } from './features/catalog/ProductListPage'
import { HomePage } from './features/home/HomePage'
import { OrderDetailPage } from './features/orders/OrderDetailPage'
import { OrderHistoryPage } from './features/orders/OrderHistoryPage'
import { PaymentMethodsPage } from './features/payment-methods/PaymentMethodsPage'
import { WishlistPage } from './features/wishlist/WishlistPage'
import { RequireAdmin, RequireAuth } from './shared/auth/RouteGuards'
import { NotFoundPage } from './shared/layout/NotFoundPage'
import { SiteLayout } from './shared/layout/SiteLayout'

export const router = createBrowserRouter([
  {
    element: <SiteLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'c/:categorySlug', element: <ProductListPage /> },
      { path: 'search', element: <ProductListPage /> },
      { path: 'products/:slug', element: <ProductDetailPage /> },
      { path: 'cart', element: <CartPage /> },
      { path: 'sign-in', element: <SignInPage /> },
      { path: 'register', element: <RegisterPage /> },
      { path: 'forgot-password', element: <ForgotPasswordPage /> },
      { path: 'reset-password', element: <ResetPasswordPage /> },
      {
        element: <RequireAuth />,
        children: [
          { path: 'account', element: <AccountPage /> },
          { path: 'account/addresses', element: <AddressesPage /> },
          { path: 'account/orders', element: <OrderHistoryPage /> },
          { path: 'account/payment-methods', element: <PaymentMethodsPage /> },
          { path: 'account/wishlist', element: <WishlistPage /> },
          // The confirmation and shipping emails link here.
          { path: 'account/orders/:id', element: <OrderDetailPage /> },
          { path: 'checkout', element: <CheckoutPage /> },
          { path: 'checkout/complete', element: <OrderConfirmationPage /> },
        ],
      },
      {
        path: 'admin',
        element: <RequireAdmin />,
        children: [
          {
            // Admin code is a separate chunk: shoppers never download it.
            lazy: async () => {
              const { AdminLayout } = await import('./features/admin/AdminLayout')
              return { Component: AdminLayout }
            },
            children: [
              {
                index: true,
                lazy: async () => {
                  const { AdminHome } = await import('./features/admin/AdminLayout')
                  return { Component: AdminHome }
                },
              },
              {
                path: 'products',
                lazy: async () => ({
                  Component: (await import('./features/admin/products/ProductListAdminPage'))
                    .ProductListAdminPage,
                }),
              },
              {
                path: 'products/:id',
                lazy: async () => ({
                  Component: (await import('./features/admin/products/ProductEditorPage'))
                    .ProductEditorPage,
                }),
              },
              {
                path: 'categories',
                lazy: async () => ({
                  Component: (await import('./features/admin/categories/CategoriesPage'))
                    .CategoriesPage,
                }),
              },
              {
                path: 'tax-and-shipping',
                lazy: async () => ({
                  Component: (await import('./features/admin/settings/TaxAndShippingPage'))
                    .TaxAndShippingPage,
                }),
              },
              {
                path: 'orders',
                lazy: async () => ({
                  Component: (await import('./features/admin/orders/OrdersAdminPage'))
                    .OrdersAdminPage,
                }),
              },
              {
                path: 'orders/:id',
                lazy: async () => ({
                  Component: (await import('./features/admin/orders/OrderAdminDetailPage'))
                    .OrderAdminDetailPage,
                }),
              },
              {
                path: 'coupons',
                lazy: async () => ({
                  Component: (await import('./features/admin/coupons/CouponsPage')).CouponsPage,
                }),
              },
              {
                path: 'reviews',
                lazy: async () => ({
                  Component: (await import('./features/admin/reviews/ReviewsAdminPage'))
                    .ReviewsAdminPage,
                }),
              },
              {
                path: 'admins',
                lazy: async () => ({
                  Component: (await import('./features/admin/admins/AdminsPage')).AdminsPage,
                }),
              },
              {
                path: 'featured',
                lazy: async () => ({
                  Component: (await import('./features/admin/featured/FeaturedProductsPage'))
                    .FeaturedProductsPage,
                }),
              },
            ],
          },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
