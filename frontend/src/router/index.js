import { createRouter, createWebHistory } from 'vue-router';
import LoginView from '../views/LoginView.vue';
import UserDashboard from '../views/UserDashboard.vue';
import AdminDashboard from '../views/AdminDashboard.vue';
import MyFilesView from '../views/MyFilesView.vue';
import FileUpload from '../components/files/FileUpload.vue';
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
    meta: { requiresGuest: true }
  },
  {
    path: '/',
    component: UserDashboard,
    meta: { requiresAuth: true, roles: [ROLES.USER, ROLES.ADMIN, ROLES.SUPER_ADMIN] },
    children: [
      { path: '', redirect: '/my-files' },
      { path: 'my-files', name: 'MyFiles', component: MyFilesView },
      { path: 'upload', name: 'UploadFile', component: FileUpload },
    ]
  },
  {
    path: '/admin',
    component: AdminDashboard,
    meta: { requiresAuth: true, roles: [ROLES.ADMIN, ROLES.SUPER_ADMIN] },
    children: [
      { path: '', redirect: '/admin/files' },
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
        component: AdminGroupManagement,
        meta: { requiresAuth: true, roles: [ROLES.SUPER_ADMIN] }
      },
      { path: 'my-files-admin', name: 'AdminMyFiles', component: MyFilesView },
      { path: 'upload-admin', name: 'AdminUploadFile', component: FileUpload },
    ]
  },
  { path: '/:catchAll(.*)', name: 'NotFound', component: NotFoundView }
];

const router = createRouter({
  history: createWebHistory(process.env.BASE_URL || '/'),
  routes
});

router.beforeEach((to, from, next) => {
  // Добавляем подробные логи
  //console.log(`>>> ROUTER GUARD: Navigating from '${from.fullPath}' to '${to.fullPath}'`);

  const requiresAuth = to.matched.some(record => record.meta.requiresAuth);
  const requiresGuest = to.matched.some(record => record.meta.requiresGuest);
  const allowedRoles = to.meta.roles;

  const isAuthenticated = !!localStorage.getItem('jwtToken');
  const userRole = localStorage.getItem('userRole');

  //console.log(`>>> ROUTER GUARD: Evaluating route: requiresAuth=${requiresAuth}, requiresGuest=${requiresGuest}`);
  //console.log(`>>> ROUTER GUARD: Current state: isAuthenticated=${isAuthenticated}, userRole='${userRole}'`);
  //console.log(`>>> ROUTER GUARD: Route allowedRoles=${allowedRoles ? allowedRoles.join(',') : 'ANY'}`);

  if (requiresAuth) {
    //console.log('>>> ROUTER GUARD: Path requires authentication.');
    if (!isAuthenticated) {
      //console.warn(`>>> ROUTER GUARD: Decision -> NOT Authenticated. Redirecting to Login.`);
      next({ name: 'Login', query: { redirect: to.fullPath } });
    } else {
      //console.log(`>>> ROUTER GUARD: User is Authenticated. Checking role...`);
      if (allowedRoles && !allowedRoles.includes(userRole)) {
        //console.warn(`>>> ROUTER GUARD: Decision -> Access DENIED (Role '${userRole}' not in [${allowedRoles.join(', ')}]). Redirecting.`);
        if (userRole === ROLES.SUPER_ADMIN || userRole === ROLES.ADMIN) {
          next({ name: 'AdminFiles' });
        } else {
          next({ name: 'MyFiles' });
        }
      } else {
        //console.log('>>> ROUTER GUARD: Decision -> Access GRANTED. Calling next().');
        next();
      }
    }
  } else if (requiresGuest) {
    //console.log('>>> ROUTER GUARD: Path requires guest.');
    if (isAuthenticated) {
      //console.warn(`>>> ROUTER GUARD: Decision -> Authenticated user on guest page. Redirecting.`);
      if (userRole === ROLES.SUPER_ADMIN || userRole === ROLES.ADMIN) {
        next({ name: 'AdminFiles' });
      } else {
        next({ name: 'MyFiles' });
      }
    } else {
      //console.log('>>> ROUTER GUARD: Decision -> Access GRANTED to guest page. Calling next().');
      next();
    }
  } else {
     //console.log('>>> ROUTER GUARD: Path is public. Calling next().');
    next();
  }
});

export default router;