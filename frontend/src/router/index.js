import { createRouter, createWebHistory } from 'vue-router';
import LoginView from '../views/LoginView.vue';
import UserDashboard from '../views/UserDashboard.vue';
import AdminDashboard from '../views/AdminDashboard.vue';
import MyFilesView from '../views/MyFilesView.vue';
import FileUpload from '../components/files/FileUpload.vue';
import GroupFiles from '../views/GroupFiles.vue'; // Убедись, что путь верный
import AdminFileBrowser from '../components/admin/AdminFileBrowser.vue';
import AdminUserManagement from '../components/admin/AdminUserManagement.vue';
import AdminGroupManagement from '../components/admin/AdminGroupManagement.vue';
import NotFoundView from '../views/NotFoundView.vue';

const ROLES = {
  SUPER_ADMIN: 'SuperAdmin',
  ADMIN: 'Admin',
  USER: 'User'
};

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: LoginView,
    meta: { requiresGuest: true } // Страница для неавторизованных
  },
  {
    // Маршрут для User Dashboard
    path: '/',
    component: UserDashboard,
    children: [
      { path: '', name: 'UserHomeRedirect', redirect: { name: 'MyFiles' } },
      { path: 'my-files', name: 'MyFiles', component: MyFilesView },
      { path: 'group-files', name: 'GroupFiles', component: GroupFiles },
      { path: 'upload', name: 'UploadFile', component: FileUpload },
    ]
  },
  {
    path: '/admin',
    component: AdminDashboard,
    meta: { requiresAuth: true, roles: [ROLES.ADMIN, ROLES.SUPER_ADMIN] }, 
    children: [
      { path: '', name: 'AdminHomeRedirect', redirect: { name: 'AdminFiles' } }, 
      {
        path: 'files',
        name: 'AdminFiles',
        component: AdminFileBrowser
      },
      {
        path: 'users',
        name: 'AdminUsers',
        component: AdminUserManagement
      },
      {
        path: 'groups',
        name: 'AdminGroups',
        component: AdminGroupManagement
      },
      { path: 'my-files', name: 'AdminMyFiles', component: MyFilesView }, 
      { path: 'upload', name: 'AdminUploadFile', component: FileUpload }, 
    ]
  },
  // 404 страница - должна быть последней
  { path: '/:catchAll(.*)', name: 'NotFound', component: NotFoundView }
];

const router = createRouter({
  history: createWebHistory(process.env.BASE_URL || '/'),
  routes
});

router.beforeEach((to, from, next) => {
  const isAuthenticated = !!localStorage.getItem('jwtToken');
  const userRole = localStorage.getItem('userRole');
  const requiresAuth = to.matched.some(record => record.meta.requiresAuth);
  const requiresGuest = to.matched.some(record => record.meta.requiresGuest);
  const allowedRoles = to.matched.reduce((acc, record) => {
      if (record.meta.roles) return acc.concat(record.meta.roles);
      return acc;
  }, []);

  if (requiresGuest && isAuthenticated) {
    console.log("GUARD: Authenticated user on guest page. Redirecting to dashboard.");
    if (userRole === ROLES.ADMIN || userRole === ROLES.SUPER_ADMIN) {
      next({ path: '/admin' }); 
    } else {
      next({ path: '/' });      
    }
    return; 
  }

  // 2. Если маршрут требует авторизации
  if (requiresAuth) {
    if (!isAuthenticated) {
      console.log("GUARD: Unauthenticated user on protected page. Redirecting to Login.");
      next({ name: 'Login', query: { redirect: to.fullPath } });
    } else {
      if (allowedRoles.length > 0 && !allowedRoles.includes(userRole)) {
        console.warn(`GUARD: Role mismatch. User ('${userRole}') cannot access route requiring [${allowedRoles.join(', ')}]. Redirecting.`);
         if (userRole === ROLES.ADMIN || userRole === ROLES.SUPER_ADMIN) {
            if (!to.path.startsWith('/admin')) {
                 next({ path: '/admin' });
            } else {
                 next({ name: 'AdminFiles' });
            }
         } else { 
             if (to.path !== '/') {
                 next({ path: '/' });
             } else {
                 console.error("GUARD: User with role 'User' denied access to '/'. Check route meta.");
                 next({ name: 'Login' }); // На всякий случай
             }
         }
      } else {
        console.log("GUARD: Access granted to protected route.");
        next();
      }
    }
    return; 
  }

  console.log("GUARD: Accessing public or guest route (user unauthenticated).");
  next();
});

export default router;